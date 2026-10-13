using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Ebay;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Scanning;
using OmniCard.Shared.Settings;
using OmniCard.Shared.Sites;
using OmniCard.Shared.Storage;
using OmniCard.Shared.Tags;
using OmniCard.Shared.Trades;
using OmniCard.Shared.Views;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>
/// Admin "Reset collection": every owned lot, location, non-default site, list, trade, tag and tag
/// rule goes; all sales data (orders, customers, listings, the movements and products realized profit
/// is computed from) and anything still listed for sale stays.
/// </summary>
public class CollectionResetServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly MockFactory _factory;
    private readonly Mock<ISalesSettingsService> _sales = new();
    private readonly ScanFolderSettings _scanFolders = new();
    private readonly CollectionResetService _reset;

    public CollectionResetServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();
        _factory = new MockFactory(_opts);

        var scanFolders = new Mock<IScanFolderSettingsService>();
        scanFolders.Setup(s => s.Get()).Returns(() => _scanFolders);
        _reset = new CollectionResetService(_factory, _sales.Object, scanFolders.Object,
            new SiteAccessService(_factory), NullLogger<CollectionResetService>.Instance);
    }

    public void Dispose() => _conn.Dispose();

    // --- helpers -------------------------------------------------------------------------------

    private OmniCardDbContext Db() => new(_opts);

    private int AddSite(string name)
    {
        using var db = Db();
        var site = new Site { Name = name };
        db.Sites.Add(site);
        db.SaveChanges();
        return site.Id;
    }

    private int AddContainer(string name, int siteId = Site.DefaultSiteId, bool isSystem = false,
        ContainerType type = ContainerType.Box)
    {
        using var db = Db();
        var c = new StorageContainer { Name = name, SiteId = siteId, IsSystem = isSystem, ContainerType = type };
        db.StorageContainers.Add(c);
        db.SaveChanges();
        return c.Id;
    }

    private int AddProduct(string name, ProductCategory category = ProductCategory.Single)
    {
        using var db = Db();
        var p = new Product
        {
            Game = CardGame.Pokemon, Category = category, Name = name,
            GameCardId = category == ProductCategory.Single ? name.ToLowerInvariant() : null,
        };
        db.Products.Add(p);
        db.SaveChanges();
        return p.Id;
    }

    /// <summary>A lot plus its Acquire movement (as every real add path writes).</summary>
    private int AddLot(int productId, int? locationId, int quantity = 1, decimal cost = 1m)
    {
        using var db = Db();
        var lot = new InventoryLot { ProductId = productId, LocationId = locationId, Quantity = quantity, UnitCost = cost };
        db.Lots.Add(lot);
        db.SaveChanges();
        db.Movements.Add(new InventoryMovement
        {
            ProductId = productId, LotId = lot.Id, Type = MovementType.Acquire, Quantity = quantity, UnitValue = cost,
        });
        db.SaveChanges();
        return lot.Id;
    }

    /// <summary>Records a sale of <paramref name="quantity"/> copies of a lot on a shipped order.</summary>
    private void Sell(int lotId, int quantity, decimal price, bool removeLot)
    {
        using var db = Db();
        var lot = db.Lots.Single(l => l.Id == lotId);
        var customer = db.Customers.FirstOrDefault() ?? db.Customers.Add(new Customer { Name = "Buyer" }).Entity;
        db.SaveChanges();
        var order = new Order
        {
            CustomerId = customer.Id, Status = OrderStatus.Shipped, ShippedAt = DateTime.UtcNow, OrderNumber = $"O-{lotId}",
        };
        db.Orders.Add(order);
        db.SaveChanges();
        var line = new OrderLine
        {
            OrderId = order.Id, LotId = lotId, ProductId = lot.ProductId, NameSnapshot = "x", Quantity = quantity,
            UnitSalePrice = price,
        };
        db.OrderLines.Add(line);
        db.SaveChanges();
        db.Movements.Add(new InventoryMovement
        {
            ProductId = lot.ProductId, LotId = lotId, Type = MovementType.Sell, Quantity = quantity,
            UnitValue = price, OrderLineId = line.Id,
        });
        db.Listings.Add(new Listing
        {
            LotId = lotId, Channel = SalesChannel.Manual, Status = ListingStatus.Sold, ListedPrice = price,
            Quantity = quantity, OrderLineId = line.Id,
        });
        lot.Quantity -= quantity;
        if (removeLot) db.Lots.Remove(lot);
        db.SaveChanges();
    }

    private void List(int lotId, ListingStatus status = ListingStatus.Listed, int? originalLocationId = null)
    {
        using var db = Db();
        db.Listings.Add(new Listing
        {
            LotId = lotId, Channel = SalesChannel.Manual, Status = status, ListedPrice = 5m,
            OriginalLocationId = originalLocationId,
        });
        db.SaveChanges();
    }

    private void Tag(int lotId, string name)
    {
        using var db = Db();
        var tag = db.Tags.FirstOrDefault(t => t.Name == name) ?? db.Tags.Add(new Tag { Name = name }).Entity;
        db.SaveChanges();
        db.LotTags.Add(new LotTag { LotId = lotId, TagId = tag.Id });
        db.SaveChanges();
    }

    private RealizedSummary Realized() => new AnalyticsService(_factory, []).GetRealized();

    // --- tests ---------------------------------------------------------------------------------

    [Fact]
    public void Reset_RequiresTheExactConfirmationPhrase()
    {
        var bulk = AddContainer("Bulk", isSystem: true, type: ContainerType.Bulk);
        AddLot(AddProduct("Pikachu"), bulk);

        Assert.Throws<InvalidOperationException>(() => _reset.Reset("reset collection", "admin"));
        Assert.Throws<InvalidOperationException>(() => _reset.Reset("", "admin"));

        using var db = Db();
        Assert.Equal(1, db.Lots.Count());
    }

    [Fact]
    public void Reset_WipesTheCollection_KeepsSalesData()
    {
        var bulk = AddContainer("Bulk", isSystem: true, type: ContainerType.Bulk);
        var binder = AddContainer("Binder", type: ContainerType.Binder);
        var shopSite = AddSite("Shop");
        var shopBox = AddContainer("Shop box", shopSite);

        // Plain collection: singles in bulk/binder/another site, plus a sealed box.
        var pika = AddProduct("Pikachu");
        var unsold = AddLot(pika, binder, quantity: 3);
        Tag(unsold, "binder-tag");
        AddLot(AddProduct("Eevee"), bulk);
        AddLot(AddProduct("Snorlax"), shopBox);
        var sealedProduct = AddProduct("Booster Box", ProductCategory.Box);
        AddLot(sealedProduct, bulk);
        using (var db = Db())
        {
            db.FlagResolutions.Add(new FlagResolution { LotId = unsold });
            db.SaveChanges();
        }

        // Sales history: a sold-out lot (already gone) and a partly sold lot (1 of 2 left).
        var charizard = AddProduct("Charizard");
        var soldOut = AddLot(charizard, binder, cost: 10m);
        Sell(soldOut, 1, 50m, removeLot: true);
        var mew = AddProduct("Mew");
        var partlySold = AddLot(mew, binder, quantity: 2, cost: 4m);
        Sell(partlySold, 1, 20m, removeLot: false);
        // A cancelled listing of a lot that's about to be wiped stays as history.
        List(unsold, ListingStatus.Cancelled);

        // Still for sale: a listed lot in another site, and a lot with an eBay listing.
        var forSaleSite = AddSite("Market");
        var forSaleBox = AddContainer("For sale", forSaleSite);
        var listedLot = AddLot(AddProduct("Mewtwo"), forSaleBox);
        List(listedLot, ListingStatus.Picked, originalLocationId: binder);
        Tag(listedLot, "sale-tag");
        var ebayLot = AddLot(AddProduct("Lugia"), bulk);
        using (var db = Db())
        {
            db.EbayListings.Add(new EbayListing { LotId = ebayLot, EbayItemId = "123", Status = EbayListingStatus.Active });
            db.SaveChanges();
        }

        // Lists, trades, tag rules, saved views on a doomed location.
        var deckBox = AddContainer("Deck", type: ContainerType.DeckBox);
        using (var db = Db())
        {
            var list = db.CardLists.Add(new CardList { Name = "Wants", Game = CardGame.Pokemon }).Entity;
            db.SaveChanges();
            db.CardListItems.Add(new CardListItem { CardListId = list.Id, GameCardId = "x", CardName = "X" });
            var session = db.TradeSessions.Add(new TradeSession { SessionRecordId = Guid.NewGuid() }).Entity;
            db.SaveChanges();
            db.Trades.Add(new Trade { TradeRecordId = Guid.NewGuid(), TradeSessionId = session.Id, CardName = "Y" });
            db.TagRules.Add(new TagRule { Name = "Rule", Query = "t:x", TagsJson = "[\"a\"]" });
            var view = db.SavedViews.Add(new SavedView { Page = SavedViewPage.Location, ContainerId = deckBox, Name = "V" }).Entity;
            db.SaveChanges();
            db.SavedViewDefaults.Add(new SavedViewDefault { Page = SavedViewPage.Location, ContainerId = deckBox, SavedViewId = view.Id });
            db.SaveChanges();
        }

        var realizedBefore = Realized();
        var preview = _reset.Preview();

        var result = _reset.Reset(CollectionResetService.ConfirmationPhrase, "admin");

        // The preview predicted exactly what happened.
        Assert.Equal(preview, result);
        Assert.Equal(5, result.Lots);               // unsold, Eevee, Snorlax, booster box, partly-sold remainder
        Assert.Equal(7, result.Units);
        Assert.Equal(2, result.KeptListedLots);
        Assert.Equal(2, result.KeptOrders);
        Assert.Equal(1, result.KeptCustomers);

        using var after = Db();
        Assert.Equal([listedLot, ebayLot], after.Lots.Select(l => l.Id).OrderBy(i => i).ToList());

        // Locations: Bulk, the listed lot's location, and its return-to location survive; the rest go.
        Assert.Equal(new[] { "Binder", "Bulk", "For sale" }, after.StorageContainers.Select(c => c.Name).OrderBy(n => n).ToArray());
        Assert.Equal(new[] { Site.DefaultSiteName, "Market" }.OrderBy(n => n), after.Sites.Select(s => s.Name).OrderBy(n => n));
        Assert.Empty(after.SavedViews);
        Assert.Empty(after.SavedViewDefaults);

        // Lists, trades, tag rules gone; only tags on surviving lots remain.
        Assert.Empty(after.CardLists);
        Assert.Empty(after.CardListItems);
        Assert.Empty(after.Trades);
        Assert.Empty(after.TradeSessions);
        Assert.Empty(after.TagRules);
        Assert.Equal(["sale-tag"], after.Tags.Select(t => t.Name).ToList());
        Assert.Single(after.LotTags);
        Assert.Empty(after.FlagResolutions);
        Assert.Single(after.EbayListings);

        // Sales data intact: orders, lines, listings history, and the realized-profit inputs.
        Assert.Equal(2, after.Orders.Count());
        Assert.Equal(2, after.OrderLines.Count());
        Assert.Equal(4, after.Listings.Count());
        Assert.True(after.Products.Any(p => p.Id == charizard));
        Assert.True(after.Products.Any(p => p.Id == mew));
        Assert.False(after.Products.Any(p => p.Id == pika));
        Assert.False(after.Products.Any(p => p.Id == sealedProduct));
        var realizedAfter = Realized();
        Assert.Equal(70m, realizedBefore.TotalProceeds); // sanity: the scenario really has sales
        Assert.Equal(14m, realizedBefore.TotalCost);
        Assert.Equal(realizedBefore.TotalSold, realizedAfter.TotalSold);
        Assert.Equal(realizedBefore.TotalProceeds, realizedAfter.TotalProceeds);
        Assert.Equal(realizedBefore.TotalCost, realizedAfter.TotalCost);
        Assert.Equal(realizedBefore.ByGame, realizedAfter.ByGame);
    }

    [Fact]
    public void Reset_KeepsLocationsReferencedBySettings()
    {
        var forSale = AddContainer("For sale shelf");
        var scanDefault = AddContainer("Scan inbox");
        var batchDefault = AddContainer("Batch target");
        AddContainer("Doomed");
        _sales.SetupGet(s => s.ForSaleLocationId).Returns(forSale);
        _scanFolders.Folders.Add(new ScanFolderConfig { Game = CardGame.Pokemon, Path = "x", DefaultContainerId = scanDefault });
        using (var db = Db())
        {
            db.ScanBatches.Add(new ScanBatch
            {
                Game = CardGame.Pokemon, Name = "B", FolderKey = "k", Status = ScanBatchStatus.Ready,
                DefaultContainerId = batchDefault,
            });
            db.SaveChanges();
        }

        var result = _reset.Reset(CollectionResetService.ConfirmationPhrase, "admin");

        Assert.Equal(1, result.Locations);
        using var after = Db();
        Assert.Equal(new[] { "Batch target", "For sale shelf", "Scan inbox" },
            after.StorageContainers.Select(c => c.Name).OrderBy(n => n).ToArray());
        Assert.Single(after.ScanBatches);
    }

    [Fact]
    public void Reset_OnAnEmptyCollection_IsANoOp()
    {
        var result = _reset.Reset(CollectionResetService.ConfirmationPhrase, "admin");

        Assert.Equal(0, result.Lots);
        Assert.Equal(0, result.Locations);
        using var db = Db();
        Assert.Single(db.Sites); // the default site always stays
    }

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
