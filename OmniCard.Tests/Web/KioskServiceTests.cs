using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Api.Contracts;
using OmniCard.Collection.Sales;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Kiosk;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Storage;
using OmniCard.Tests.Services.Ebay;
using OmniCard.Web.Api.Controllers;
using OmniCard.Web.Services.Kiosk;

namespace OmniCard.Tests.Web;

/// <summary>The customer kiosk: unique session names, holds that keep two kiosks off the same copy, idle
/// expiry, and the staff queue turning a pick list into an order.</summary>
public class KioskServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly Factory _factory;
    private readonly Settings _settings = new();
    private readonly Clock _clock = new();
    private readonly KioskService _kiosk;
    private readonly int _shop;
    private readonly int _deviceA;
    private readonly int _deviceB;

    public KioskServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts))
            ctx.Database.EnsureCreated();
        _factory = new Factory(_opts);
        _kiosk = new KioskService(_factory, _settings, new PriceByCardId(), _clock);

        _shop = SeedContainer("Shop binder");
        _settings.Value.LocationIds = [_shop];
        _deviceA = _kiosk.Enroll("Front", "admin").Id;
        _deviceB = _kiosk.Enroll("Back", "admin").Id;
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public void StartSession_NamesAreUniqueAmongOpenSessions_IgnoringCaseAndSpacing()
    {
        _kiosk.StartSession(_deviceA, "Ada  Lovelace");

        var ex = Assert.Throws<KioskException>(() => _kiosk.StartSession(_deviceB, " ada lovelace "));
        Assert.Equal("name-taken", ex.Code);

        _kiosk.EndSession(_deviceA);
        Assert.Equal("ada lovelace", _kiosk.StartSession(_deviceB, " ada lovelace ").CustomerName);
    }

    [Fact]
    public void StartSession_RefusedWhileKioskIsDisabled()
    {
        _settings.Value.Enabled = false;
        Assert.Equal("closed", Assert.Throws<KioskException>(() => _kiosk.StartSession(_deviceA, "Ada")).Code);
    }

    [Fact]
    public void Search_ShowsOnlyConfiguredLocations_OneRowPerConditionAndLanguage()
    {
        var product = SeedProduct("Sol Ring", "C21", "sol");
        SeedLot(product, _shop, quantity: 2);
        SeedLot(product, _shop, quantity: 1);                 // same group → stacked
        SeedLot(product, _shop, quantity: 1, condition: "LP");
        SeedLot(product, _shop, quantity: 1, language: "ja");
        SeedLot(product, SeedContainer("Personal"), quantity: 5);
        SeedLot(SeedProduct("Lightning Bolt", "M10"), _shop);

        var rows = Search("sol").Items;

        Assert.Equal(3, rows.Count);
        var nm = Assert.Single(rows, r => r.Condition == "NM" && r.Language == "en");
        Assert.Equal(3, nm.Available);
        Assert.Equal(5m, nm.Price);
        Assert.Single(rows, r => r.Condition == "LP");
        Assert.Single(rows, r => r.Language == "ja");
    }

    [Fact]
    public void Search_LeavesOutCopiesListedOnExternalChannels_ButNotInStoreListings()
    {
        var product = SeedProduct("Sol Ring", "C21");
        var ebay = SeedLot(product, _shop, quantity: 2);
        SeedListing(ebay, SalesChannel.Ebay, quantity: 2);
        var store = SeedLot(product, _shop, quantity: 1, condition: "LP");
        SeedListing(store, SalesChannel.Manual, quantity: 1);

        var row = Assert.Single(Search("sol").Items);
        Assert.Equal("LP", row.Condition);
    }

    [Fact]
    public void AddHold_TwoKiosksCanNeverHoldTheSameCopy()
    {
        var product = SeedProduct("Sol Ring", "C21");
        SeedLot(product, _shop, quantity: 1);
        var key = Assert.Single(Search("sol").Items).Key;

        _kiosk.StartSession(_deviceA, "Ada");
        _kiosk.StartSession(_deviceB, "Bob");
        Assert.Equal(1, _kiosk.AddHold(_deviceA, key, 1).TotalQuantity);

        var ex = Assert.Throws<KioskException>(() => _kiosk.AddHold(_deviceB, key, 1));
        Assert.Equal("unavailable", ex.Code);
        Assert.Empty(Search("sol", _deviceB).Items);

        // The holder still sees it (as on their list).
        var mine = Assert.Single(Search("sol", _deviceA).Items);
        Assert.Equal(0, mine.Available);
        Assert.Equal(1, mine.InMyList);
    }

    [Fact]
    public void AddHold_SpreadsAcrossLots_AndRemoveHoldReleasesCopies()
    {
        var product = SeedProduct("Sol Ring", "C21");
        SeedLot(product, _shop, quantity: 1);
        SeedLot(product, _shop, quantity: 2);
        var key = Assert.Single(Search("sol").Items).Key;
        _kiosk.StartSession(_deviceA, "Ada");

        var session = _kiosk.AddHold(_deviceA, key, 3);
        Assert.Equal(3, session.TotalQuantity);
        Assert.Equal(15m, session.TotalPrice);
        Assert.Equal("unavailable", Assert.Throws<KioskException>(() => _kiosk.AddHold(_deviceA, key, 1)).Code);

        Assert.Equal(1, _kiosk.RemoveHold(_deviceA, key, 2).TotalQuantity);
        _kiosk.StartSession(_deviceB, "Bob");
        Assert.Equal(2, _kiosk.AddHold(_deviceB, key, 2).TotalQuantity);
    }

    [Fact]
    public void IdleSession_Expires_ReleasingItsHoldsAndName()
    {
        var product = SeedProduct("Sol Ring", "C21");
        SeedLot(product, _shop, quantity: 1);
        var key = Assert.Single(Search("sol").Items).Key;
        _kiosk.StartSession(_deviceA, "Ada");
        _kiosk.AddHold(_deviceA, key, 1);

        _clock.Advance(TimeSpan.FromMinutes(_settings.Value.IdleTimeoutMinutes + 1));

        Assert.Null(_kiosk.GetSession(_deviceA));
        _kiosk.StartSession(_deviceB, "Ada");
        Assert.Equal(1, _kiosk.AddHold(_deviceB, key, 1).TotalQuantity);
    }

    [Fact]
    public void Touch_KeepsABrowsingSessionAlive()
    {
        _kiosk.StartSession(_deviceA, "Ada");
        _clock.Advance(TimeSpan.FromMinutes(_settings.Value.IdleTimeoutMinutes - 1));
        Assert.NotNull(_kiosk.Touch(_deviceA));
        _clock.Advance(TimeSpan.FromMinutes(_settings.Value.IdleTimeoutMinutes - 1));
        Assert.NotNull(_kiosk.GetSession(_deviceA));
    }

    [Fact]
    public void Submit_QueuesTheListWithPickLocations_AndItNeverExpires()
    {
        var product = SeedProduct("Sol Ring", "C21");
        SeedLot(product, _shop, quantity: 1, page: 3, slot: 7);
        var key = Assert.Single(Search("sol").Items).Key;
        _kiosk.StartSession(_deviceA, "Ada");
        _kiosk.AddHold(_deviceA, key, 1);

        var result = _kiosk.Submit(_deviceA);
        Assert.Equal(1, result.ItemCount);
        Assert.Null(_kiosk.GetSession(_deviceA)); // the kiosk is free for the next customer

        _clock.Advance(TimeSpan.FromHours(6));
        var entry = Assert.Single(_kiosk.GetQueue());
        Assert.Equal("Queued", entry.Status);
        Assert.Equal("Front", entry.DeviceName);
        var line = Assert.Single(entry.Lines);
        Assert.Equal("Shop binder", line.LocationName);
        Assert.Equal((3, 7), (line.Page, line.Slot));
        Assert.Equal(new KioskQueueCountDto(1, 0), _kiosk.GetQueueCount());

        // Still held: nobody else can take it while it waits at the counter.
        Assert.Empty(Search("sol", _deviceB).Items);
    }

    [Fact]
    public void Submit_RefusesAnEmptyList()
    {
        _kiosk.StartSession(_deviceA, "Ada");
        Assert.Equal("empty", Assert.Throws<KioskException>(() => _kiosk.Submit(_deviceA)).Code);
    }

    [Fact]
    public void CreateOrder_TurnsTheListIntoAnOrderAtTheHeldPrices()
    {
        var product = SeedProduct("Sol Ring", "C21");
        var lot = SeedLot(product, _shop, quantity: 3);
        var key = Assert.Single(Search("sol").Items).Key;
        var sessionId = QueueList(_deviceA, "Ada", key, 2);
        _kiosk.Claim(sessionId, 1, "staff");

        var result = _kiosk.CreateOrder(sessionId, customerId: null, customerName: null);

        using var ctx = new OmniCardDbContext(_opts);
        var order = ctx.Orders.Single(o => o.Id == result.OrderId);
        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Equal(SalesChannel.Manual, order.Channel);
        Assert.Equal("Ada", ctx.Customers.Single(c => c.Id == result.CustomerId).Name);
        var line = Assert.Single(ctx.OrderLines.Where(l => l.OrderId == order.Id));
        Assert.Equal((lot, 2, 5m), (line.LotId!.Value, line.Quantity, line.UnitSalePrice));
        Assert.Empty(ctx.KioskHolds);
        var session = ctx.KioskSessions.Single(s => s.Id == sessionId);
        Assert.Equal((KioskSessionStatus.Completed, (int?)order.Id, (string?)null), (session.Status, session.OrderId, session.ActiveNameKey));

        // The open order keeps the two copies reserved; one is left for the next customer.
        Assert.Equal(1, Assert.Single(Search("sol", _deviceB).Items).Available);
        Assert.Empty(_kiosk.GetQueue());
    }

    [Fact]
    public void CreateOrder_UsesAnExistingCustomerWhenChosen()
    {
        int customerId;
        using (var ctx = new OmniCardDbContext(_opts))
        {
            var c = new Customer { Name = "Ada Lovelace" };
            ctx.Customers.Add(c);
            ctx.SaveChanges();
            customerId = c.Id;
        }
        SeedLot(SeedProduct("Sol Ring", "C21"), _shop);
        var sessionId = QueueList(_deviceA, "Ada", Assert.Single(Search("sol").Items).Key, 1);

        Assert.Equal(customerId, _kiosk.CreateOrder(sessionId, customerId, null).CustomerId);
        using var check = new OmniCardDbContext(_opts);
        Assert.Equal(1, check.Customers.Count());
    }

    [Fact]
    public void CreateOrder_FailsWithoutWritingWhenACardWasSoldMeanwhile()
    {
        var lot = SeedLot(SeedProduct("Sol Ring", "C21"), _shop, quantity: 1);
        var sessionId = QueueList(_deviceA, "Ada", Assert.Single(Search("sol").Items).Key, 1);
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Lots.Single(l => l.Id == lot).Quantity = 0;
            ctx.SaveChanges();
        }

        var ex = Assert.Throws<KioskException>(() => _kiosk.CreateOrder(sessionId, null, null));
        Assert.Equal("unavailable", ex.Code);
        using var check = new OmniCardDbContext(_opts);
        Assert.Empty(check.Orders);
        Assert.Empty(check.Customers);
    }

    [Fact]
    public void StaffCanTrimAndCancelAList()
    {
        var product = SeedProduct("Sol Ring", "C21");
        SeedLot(product, _shop, quantity: 2);
        var sessionId = QueueList(_deviceA, "Ada", Assert.Single(Search("sol").Items).Key, 2);
        var hold = Assert.Single(Assert.Single(_kiosk.GetQueue()).Lines);

        _kiosk.RemoveQueueHold(sessionId, hold.HoldId, 1);
        Assert.Equal(1, Assert.Single(_kiosk.GetQueue()).TotalQuantity);

        _kiosk.Cancel(sessionId);
        Assert.Empty(_kiosk.GetQueue());
        Assert.Equal(2, Assert.Single(Search("sol", _deviceB).Items).Available);
    }

    [Fact]
    public void Claim_OnlyForSubmittedLists()
    {
        _kiosk.StartSession(_deviceA, "Ada");
        var id = Assert.Single(_kiosk.GetQueue()).Id;
        Assert.Equal("wrong-status", Assert.Throws<KioskException>(() => _kiosk.Claim(id, 1, "staff")).Code);
    }

    [Fact]
    public void Revoke_LocksTheDeviceOut_AndReleasesItsCustomer()
    {
        _kiosk.StartSession(_deviceA, "Ada");

        Assert.True(_kiosk.Revoke(_deviceA));

        Assert.Null(_kiosk.GetActiveDevice(_deviceA));
        Assert.NotNull(_kiosk.GetActiveDevice(_deviceB));
        Assert.Equal("Ada", _kiosk.StartSession(_deviceB, "Ada").CustomerName);
    }

    [Fact]
    public void OrderEditor_CannotSellACopyHeldAtTheKiosk()
    {
        var product = SeedProduct("Sol Ring", "C21");
        var lot = SeedLot(product, _shop, quantity: 1);
        SeedListing(lot, SalesChannel.Manual, quantity: 1);
        var key = Assert.Single(Search("sol").Items).Key;
        _kiosk.StartSession(_deviceA, "Ada");
        _kiosk.AddHold(_deviceA, key, 1);

        var salesSettings = new Mock<ISalesSettingsService>().Object;
        var orders = new OrderService(_factory, new ListingService(_factory, salesSettings), new FakeEbayListingService(),
            NullLogger<OrderService>.Instance);
        var controller = new OrdersController(orders, new CustomerService(_factory), salesSettings, null!, null!, _factory, _kiosk);

        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<SellableListingDto>>(controller.Sellable("sol", "Mtg").Value));
    }

    [Fact]
    public void SettingsNormalize_ClampsTimeoutAndCleansLocations()
    {
        var s = new KioskSettings { IdleTimeoutMinutes = 0, LocationIds = [3, 3, -1, 0, 4], WelcomeMessage = "  " };
        KioskSettingsService.Normalize(s);
        Assert.Equal(KioskSettings.MinIdleTimeoutMinutes, s.IdleTimeoutMinutes);
        Assert.Equal([3, 4], s.LocationIds);
        Assert.Null(s.WelcomeMessage);
    }

    private PagedResult<KioskCardDto> Search(string q, int? device = null) =>
        _kiosk.Search(device ?? _deviceA, CardGame.Mtg, q, null, 0, 50);

    private int QueueList(int device, string name, string key, int quantity)
    {
        var id = _kiosk.StartSession(device, name).Id;
        _kiosk.AddHold(device, key, quantity);
        _kiosk.Submit(device);
        return id;
    }

    private int SeedContainer(string name)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var c = new StorageContainer { Name = name, ContainerType = ContainerType.Binder };
        ctx.StorageContainers.Add(c);
        ctx.SaveChanges();
        return c.Id;
    }

    private int SeedProduct(string name, string setCode, string? gameCardId = null)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var p = new Product
        {
            Game = CardGame.Mtg, Category = ProductCategory.Single, Name = name, SetCode = setCode, SetName = setCode,
            GameCardId = gameCardId ?? name.ToLowerInvariant(),
        };
        ctx.Products.Add(p);
        ctx.SaveChanges();
        return p.Id;
    }

    private int SeedLot(int productId, int containerId, int quantity = 1, string condition = "NM", string? language = null,
        int? page = null, int? slot = null)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var lot = new InventoryLot
        {
            ProductId = productId, LocationId = containerId, Quantity = quantity, Condition = condition, Language = language,
            Page = page, Slot = slot,
        };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    private void SeedListing(int lotId, SalesChannel channel, int quantity)
    {
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Listings.Add(new Listing { LotId = lotId, Channel = channel, Status = ListingStatus.Listed, ListedPrice = 1m, Quantity = quantity });
        ctx.SaveChanges();
    }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> o) : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }

    private sealed class Settings : IKioskSettingsService
    {
        public KioskSettings Value { get; } = new();
        public KioskSettings Get() => Value;
        public void Save(KioskSettings settings) { }
    }

    /// <summary>Every card costs $5; art is left as stored.</summary>
    private sealed class PriceByCardId : IKioskCardEnricher
    {
        public void Enrich(IReadOnlyList<CollectionCard> cards, bool prices)
        {
            if (prices) foreach (var c in cards) c.MarketPrice = 5m;
        }
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
