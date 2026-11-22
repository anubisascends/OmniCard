using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Ebay;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Sites;
using OmniCard.Shared.Storage;
using OmniCard.Web.Services;
using static OmniCard.Web.Services.LocationBulkService;

namespace OmniCard.Tests.Web;

/// <summary>
/// Locations page ▸ Select: bulk game/sets, site and flag edits are all or nothing, and the bulk delete only
/// runs against the exact state that was previewed, with the typed counts, and never deletes cards that
/// something else is counting on.
/// </summary>
public class LocationBulkServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly LocationBulkService _svc;
    private readonly int _bulkId;

    public LocationBulkServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Database.EnsureCreated();
            var bulk = new StorageContainer { Name = "Bulk", ContainerType = ContainerType.Bulk, IsSystem = true };
            ctx.StorageContainers.Add(bulk);
            ctx.SaveChanges();
            _bulkId = bulk.Id;
        }
        var factory = new Factory(_opts);
        _svc = new LocationBulkService(factory, new SavedViewService(factory, TimeProvider.System),
            NullLogger<LocationBulkService>.Instance);
    }

    public void Dispose() => _conn.Dispose();

    private OmniCardDbContext Db() => new(_opts);

    private int AddLocation(string name, ContainerType type = ContainerType.Box, CardGame? game = null,
        string? sets = null, int siteId = Site.DefaultSiteId)
    {
        using var db = Db();
        var c = new StorageContainer { Name = name, ContainerType = type, Game = game, DefaultSets = sets, SiteId = siteId };
        db.StorageContainers.Add(c);
        db.SaveChanges();
        return c.Id;
    }

    private int AddLot(int locationId, CardGame game = CardGame.Mtg, int qty = 1, decimal price = 1m,
        ProductCategory category = ProductCategory.Single)
    {
        using var db = Db();
        var p = new Product { Game = game, Category = category, Name = "Card", LastMarketPrice = price };
        db.Products.Add(p);
        db.SaveChanges();
        var lot = new InventoryLot { ProductId = p.Id, Quantity = qty, LocationId = locationId };
        db.Lots.Add(lot);
        db.SaveChanges();
        return lot.Id;
    }

    private StorageContainer? Location(int id) => Db().StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == id);

    private static LocationBulkException Throws(Action act) => Assert.Throws<LocationBulkException>(act);

    // --- game & sets ---

    [Fact]
    public void CardFilter_KeepGame_AddSets_MergesIntoEachLocationsSets()
    {
        var a = AddLocation("A", game: CardGame.Mtg, sets: "mh3");
        var b = AddLocation("B", game: CardGame.Mtg);

        var result = _svc.SetCardFilter(new BulkLocationCardFilterRequest
        {
            Ids = [a, b], GameMode = "keep", SetsMode = "add", Sets = ["BLB", "mh3"],
        });

        Assert.Equal(2, result.Changed);
        Assert.Equal(["mh3", "blb"], Location(a)!.DefaultSetCodes);
        Assert.Equal(["blb", "mh3"], Location(b)!.DefaultSetCodes);
    }

    [Fact]
    public void CardFilter_LocationWithAnotherGamesCards_FailsAndSavesNothing()
    {
        var clean = AddLocation("Clean");
        var mixed = AddLocation("Mixed");
        AddLot(mixed, CardGame.Pokemon);

        var ex = Throws(() => _svc.SetCardFilter(new BulkLocationCardFilterRequest
        {
            Ids = [clean, mixed], GameMode = "set", Game = "Mtg",
        }));

        Assert.Equal(ErrorKind.Failures, ex.Kind);
        Assert.Equal(mixed, Assert.Single(ex.Failures).Id);
        Assert.Null(Location(clean)!.Game); // the valid one wasn't changed either
    }

    [Fact]
    public void CardFilter_ReplaceSetsAcrossDifferentGames_IsRefused()
    {
        var a = AddLocation("A", game: CardGame.Mtg);
        var b = AddLocation("B", game: CardGame.Pokemon);

        var ex = Throws(() => _svc.SetCardFilter(new BulkLocationCardFilterRequest
        {
            Ids = [a, b], GameMode = "keep", SetsMode = "replace", Sets = ["mh3"],
        }));

        Assert.Equal(ErrorKind.BadRequest, ex.Kind);
    }

    [Fact]
    public void CardFilter_AnyGameOnDeckBox_Fails_AndGameChangeDropsOldSets()
    {
        var box = AddLocation("Box", game: CardGame.Mtg, sets: "mh3");
        var deck = AddLocation("Deck", ContainerType.DeckBox, CardGame.Mtg);

        var ex = Throws(() => _svc.SetCardFilter(new BulkLocationCardFilterRequest { Ids = [box, deck], GameMode = "any" }));
        Assert.Equal(deck, Assert.Single(ex.Failures).Id);

        _svc.SetCardFilter(new BulkLocationCardFilterRequest { Ids = [box], GameMode = "set", Game = "Pokemon" });
        Assert.Equal(CardGame.Pokemon, Location(box)!.Game);
        Assert.Empty(Location(box)!.DefaultSetCodes);
    }

    // --- site + flags ---

    [Fact]
    public void SetSite_IncludingBulk_FailsAndMovesNothing()
    {
        int site;
        using (var db = Db())
        {
            var s = new Site { Name = "Shop" };
            db.Sites.Add(s);
            db.SaveChanges();
            site = s.Id;
        }
        var a = AddLocation("A");

        var ex = Throws(() => _svc.SetSite([a, _bulkId], site));
        Assert.Equal(ErrorKind.Failures, ex.Kind);
        Assert.Equal(Site.DefaultSiteId, Location(a)!.SiteId);

        Assert.Equal(1, _svc.SetSite([a], site).Changed);
        Assert.Equal(site, Location(a)!.SiteId);
    }

    [Fact]
    public void SetSite_IntoASiteThatHasTheNameForThatGame_FailsAndMovesNothing()
    {
        int site;
        using (var db = Db())
        {
            var s = new Site { Name = "Shop" };
            db.Sites.Add(s);
            db.SaveChanges();
            site = s.Id;
        }
        AddLocation("Sales Binder", game: CardGame.Mtg, siteId: site);
        var clash = AddLocation("Sales Binder", game: CardGame.Mtg);
        var fine = AddLocation("Sales Binder", game: CardGame.Pokemon);

        var ex = Throws(() => _svc.SetSite([clash, fine], site));

        Assert.Equal(ErrorKind.Failures, ex.Kind);
        Assert.Equal(clash, Assert.Single(ex.Failures).Id);
        Assert.Equal(Site.DefaultSiteId, Location(fine)!.SiteId);
    }

    [Fact]
    public void SetCardFilter_TwoSelectedLocationsLandingOnOneNameAndGame_Fails()
    {
        var a = AddLocation("Sales Binder");
        var b = AddLocation("Sales Binder", game: CardGame.Pokemon);

        var ex = Throws(() => _svc.SetCardFilter(new BulkLocationCardFilterRequest { Ids = [a, b], GameMode = "set", Game = "Mtg" }));

        Assert.Equal(ErrorKind.Failures, ex.Kind);
        Assert.Single(ex.Failures);
        Assert.Null(Location(a)!.Game);
        Assert.Equal(CardGame.Pokemon, Location(b)!.Game);
    }

    [Fact]
    public void AlwaysAvailable_CountsChangedAndLeavesBulkAlone()
    {
        var a = AddLocation("A");
        var b = AddLocation("B");
        _svc.SetAlwaysAvailable([a], true);

        var result = _svc.SetAlwaysAvailable([a, b, _bulkId], true);

        Assert.Equal(new LocationBulkResultDto(1, 2), result);
        Assert.True(Location(b)!.AlwaysAvailable);
        Assert.False(Location(_bulkId)!.AlwaysAvailable);
    }

    [Fact]
    public void UnknownId_IsNotFound()
    {
        var a = AddLocation("A");
        Assert.Equal(ErrorKind.NotFound, Throws(() => _svc.SetIgnoredForLists([a, 9999], true)).Kind);
        Assert.False(Location(a)!.ExcludeFromDeckCheck);
    }

    // --- delete ---

    [Fact]
    public void Preview_CountsEveryGamesCards_SealedLots_AndListedCopies()
    {
        var a = AddLocation("A");
        AddLot(a, CardGame.Mtg, qty: 3, price: 2m);
        var listedLot = AddLot(a, CardGame.Pokemon, qty: 2, price: 5m);
        AddLot(a, CardGame.Mtg, category: ProductCategory.Box);
        using (var db = Db())
        {
            db.Listings.Add(new Listing { LotId = listedLot, Quantity = 1, Status = ListingStatus.Listed, Channel = SalesChannel.TcgPlayer });
            db.SaveChanges();
        }

        var p = _svc.PreviewDelete([a], moveCardsToBulk: true);

        var row = Assert.Single(p.Locations);
        Assert.Equal(2, row.Lots);
        Assert.Equal(5, row.Copies);
        Assert.Equal(16m, row.MarketValue);
        Assert.Equal(1, row.SealedLots);
        Assert.Equal(1, row.ListedCopies);
        Assert.Equal(p.Token, _svc.PreviewDelete([a], true).Token);
        Assert.NotEqual(p.Token, _svc.PreviewDelete([a], false).Token);
    }

    [Fact]
    public void Delete_MoveToBulk_WithMatchingPreview_MovesCardsAndRemovesLocations()
    {
        var a = AddLocation("A");
        var b = AddLocation("B");
        var lot = AddLot(a, qty: 4);
        var p = _svc.PreviewDelete([a, b], true);

        var result = _svc.Delete(new BulkLocationDeleteRequest
        {
            Ids = [a, b], MoveCardsToBulk = true, Token = p.Token, ConfirmLocationCount = 2,
        }, "tester");

        Assert.Equal(new BulkLocationDeleteResultDto(2, 4, 0), result);
        Assert.Null(Location(a));
        Assert.Null(Location(b));
        Assert.Equal(_bulkId, Db().Lots.Single(l => l.Id == lot).LocationId);
    }

    [Fact]
    public void Delete_AfterTheLocationChanged_IsStale_AndDeletesNothing()
    {
        var a = AddLocation("A");
        var p = _svc.PreviewDelete([a], true);
        AddLot(a); // a card arrives after the review

        var ex = Throws(() => _svc.Delete(new BulkLocationDeleteRequest
        {
            Ids = [a], Token = p.Token, ConfirmLocationCount = 1,
        }, "tester"));

        Assert.Equal(ErrorKind.Stale, ex.Kind);
        Assert.NotNull(Location(a));
    }

    [Fact]
    public void Delete_WrongTypedCounts_AreRefused()
    {
        var a = AddLocation("A");
        AddLot(a, qty: 2);
        var keep = _svc.PreviewDelete([a], true);
        Assert.Equal(ErrorKind.BadRequest, Throws(() => _svc.Delete(new BulkLocationDeleteRequest
        {
            Ids = [a], Token = keep.Token, ConfirmLocationCount = 2,
        }, null)).Kind);

        var drop = _svc.PreviewDelete([a], false);
        Assert.Equal(ErrorKind.BadRequest, Throws(() => _svc.Delete(new BulkLocationDeleteRequest
        {
            Ids = [a], MoveCardsToBulk = false, Token = drop.Token, ConfirmLocationCount = 1, ConfirmCopies = 1,
        }, null)).Kind);
        Assert.NotNull(Location(a));
    }

    [Fact]
    public void Delete_Cards_RefusedWhileCopiesAreOnAnOpenOrderOrLiveOnEbay()
    {
        var ordered = AddLocation("Ordered");
        var orderedLot = AddLot(ordered);
        var ebay = AddLocation("Ebay");
        var ebayLot = AddLot(ebay);
        using (var db = Db())
        {
            var customer = new Customer { Name = "Buyer" };
            db.Customers.Add(customer);
            db.SaveChanges();
            var order = new Order { CustomerId = customer.Id, Status = OrderStatus.Created };
            db.Orders.Add(order);
            db.SaveChanges();
            db.OrderLines.Add(new OrderLine { OrderId = order.Id, LotId = orderedLot, Quantity = 1, NameSnapshot = "Card" });
            db.EbayListings.Add(new EbayListing { LotId = ebayLot, EbayItemId = "1", Status = EbayListingStatus.Active });
            db.SaveChanges();
        }
        var p = _svc.PreviewDelete([ordered, ebay], false);
        Assert.Equal(1, p.TotalOpenOrderCopies);
        Assert.Equal(1, p.TotalListedCopies);

        var ex = Throws(() => _svc.Delete(new BulkLocationDeleteRequest
        {
            Ids = [ordered, ebay], MoveCardsToBulk = false, Token = p.Token, ConfirmLocationCount = 2, ConfirmCopies = 2,
        }, null));

        Assert.Equal(ErrorKind.Failures, ex.Kind);
        Assert.Equal(2, ex.Failures.Count);
        Assert.Equal(2, Db().Lots.Count(l => l.LocationId == ordered || l.LocationId == ebay));
    }

    [Fact]
    public void Delete_Cards_WithEverythingConfirmed_DeletesTheLots()
    {
        var a = AddLocation("A");
        AddLot(a, qty: 3);
        var p = _svc.PreviewDelete([a], false);

        var result = _svc.Delete(new BulkLocationDeleteRequest
        {
            Ids = [a], MoveCardsToBulk = false, Token = p.Token, ConfirmLocationCount = 1, ConfirmCopies = 3,
        }, null);

        Assert.Equal(new BulkLocationDeleteResultDto(1, 0, 3), result);
        Assert.Empty(Db().Lots.Where(l => l.Product.Category == ProductCategory.Single));
    }

    [Fact]
    public void Delete_IncludingBulk_IsRefused()
    {
        var a = AddLocation("A");
        var ex = Throws(() => _svc.Delete(new BulkLocationDeleteRequest
        {
            Ids = [a, _bulkId], Token = "x", ConfirmLocationCount = 2,
        }, null));
        Assert.Equal(ErrorKind.Failures, ex.Kind);
        Assert.NotNull(Location(a));
    }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
