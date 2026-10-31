using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Intake;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Settings;
using OmniCard.Shared.Storage;
using OmniCard.Shared.Tags;
using OmniCard.Web.Services;
using OmniCard.Web.Services.Intake;

namespace OmniCard.Tests.Web.Intake;

/// <summary>Quoting and buying singles: server-side pricing from the catalog, the bought lots, the audit
/// record and the store credit, plus the guards (customer for trade, no location, prices moved).</summary>
public class IntakeServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly Factory _factory;
    private readonly FakeSettings _settings = new();
    private readonly StoreCreditService _credit;
    private readonly IntakeService _intake;
    private readonly int _location;
    private readonly int _customer;

    // Catalog: card "a" market 10 / low 6; card "b" market 4 (no low); card "x" no price at all.
    public IntakeServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();
        _factory = new Factory(_opts);

        var game = new Mock<ICardGameService>();
        game.SetupGet(g => g.Game).Returns(CardGame.Mtg);
        game.Setup(g => g.GetCurrentPrices(It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
            .Returns((IEnumerable<string> ids, bool _) => Prices(ids, new() { ["a"] = 10m, ["b"] = 4m }));
        game.As<ILowPriceSource>().Setup(g => g.GetLowPrices(It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
            .Returns((IEnumerable<string> ids, bool _) => Prices(ids, new() { ["a"] = 6m }));
        var cardService = new Mock<ICardService>();
        cardService.Setup(c => c.GetGameService(CardGame.Mtg)).Returns(game.Object);

        var binderCards = new WebBinderCardService(_factory, new StubDataPath());
        var commits = new ScanCommitService(cardService.Object, binderCards, new Mock<ITagService>().Object,
            NullLogger<ScanCommitService>.Instance);
        _credit = new StoreCreditService(_factory);
        _intake = new IntakeService(_factory, _settings, cardService.Object, commits, _credit,
            NullLogger<IntakeService>.Instance);

        using var db = _factory.CreateDbContext();
        var box = new StorageContainer { Name = "Intake", ContainerType = ContainerType.Box };
        var customer = new Customer { Name = "Sam Seller" };
        db.StorageContainers.Add(box);
        db.Customers.Add(customer);
        db.SaveChanges();
        _location = box.Id;
        _customer = customer.Id;
        _settings.Value.LocationId = _location;
    }

    public void Dispose() => _conn.Dispose();

    private static Dictionary<string, decimal> Prices(IEnumerable<string> ids, Dictionary<string, decimal> catalog) =>
        ids.Where(catalog.ContainsKey).ToDictionary(id => id, id => catalog[id]);

    private static IntakeQuoteLineRequest Line(string id, string payout = "Cash", int qty = 1, string condition = "NM",
        decimal? manual = null, bool? excluded = null) => new()
    {
        Item = new ScanCommitItem { Game = "Mtg", GameCardId = id, Name = "Card " + id, SetCode = "tst", Quantity = qty, Condition = condition },
        Payout = payout,
        ManualPrice = manual,
        Excluded = excluded,
    };

    private IntakeAcceptRequest Accept(IReadOnlyList<IntakeQuoteLineRequest> lines, int? customerId = null, string basis = "Market")
    {
        var quote = _intake.Quote(new IntakeQuoteRequest { PriceBasis = basis, Lines = lines });
        return new IntakeAcceptRequest
        {
            PriceBasis = basis, Lines = lines, CustomerId = customerId,
            ExpectedCashPaid = quote.CashPaid, ExpectedTradePaid = quote.TradePaid,
        };
    }

    [Fact]
    public void Quote_Prices_From_The_Catalog_With_Both_Offers()
    {
        var quote = _intake.Quote(new IntakeQuoteRequest { Lines = [Line("a", qty: 2), Line("b")] });

        Assert.Equal(3, quote.CardCount);
        Assert.Equal(24m, quote.MarketTotal);
        Assert.Equal(16.8m, quote.CashOffer);   // 24 × 70%
        Assert.Equal(19.2m, quote.TradeOffer);  // 24 × 80%
        Assert.False(quote.SingleOffer);
        Assert.Equal(6m, quote.Lines[0].LowPrice);
    }

    [Fact]
    public void Quote_Low_Basis_Uses_Low_Then_Market()
    {
        var quote = _intake.Quote(new IntakeQuoteRequest { PriceBasis = "Low", Lines = [Line("a"), Line("b")] });

        Assert.Equal(6m, quote.Lines[0].BasePrice);
        Assert.Equal(4m, quote.Lines[1].BasePrice);
        Assert.True(quote.Lines[1].UsedMarketFallback);
    }

    [Fact]
    public void Quote_Flags_Single_Offer_When_Percentages_Match()
    {
        _settings.Value.TradePercent = _settings.Value.CashPercent;
        Assert.True(_intake.Quote(new IntakeQuoteRequest { Lines = [Line("a")] }).SingleOffer);
    }

    [Fact]
    public void Accept_Buys_Cards_Records_The_Intake_And_Credits_Trade()
    {
        var result = _intake.Accept(Accept([Line("a", qty: 2), Line("b", payout: "Trade", condition: "LP")], _customer), 7, "clerk");

        Assert.Equal(3, result.CardCount);
        Assert.Equal(14m, result.CashPaid);     // 2 × 10 × 70%
        Assert.Equal(2.72m, result.TradePaid);  // 4 × 85% × 80%
        Assert.Equal(2.72m, result.CustomerCreditBalance);

        using var db = _factory.CreateDbContext();
        var lots = db.Lots.Include(l => l.Product).Where(l => l.LocationId == _location).OrderBy(l => l.Id).ToList();
        Assert.Equal(2, lots.Count);
        Assert.Equal(2, lots[0].Quantity);
        Assert.Equal(7m, lots[0].UnitCost);
        Assert.Equal(2.72m, lots[1].UnitCost);
        Assert.Equal("LP", lots[1].Condition);
        Assert.All(lots, l => Assert.Equal($"Intake #{result.IntakeId}", l.Source));

        var record = db.Intakes.Single();
        Assert.Equal("clerk", record.CreatedByName);
        Assert.Equal(7, record.CreatedByUserId);
        Assert.Equal("Sam Seller", record.CustomerName);
        Assert.Equal("Intake", record.LocationName);
        Assert.Equal(24m, record.MarketTotal);
        var lines = db.IntakeLines.OrderBy(l => l.Id).ToList();
        Assert.Equal(lots.Select(l => (int?)l.Id), lines.Select(l => l.LotId));
        Assert.Equal(IntakePayout.Trade, lines[1].Payout);
        Assert.Equal(85m, lines[1].ConditionPercent);

        var credit = db.StoreCreditEntries.Single();
        Assert.Equal(record.Id, credit.IntakeId);
        Assert.Equal(_customer, credit.CustomerId);
    }

    [Fact]
    public void Accept_Skips_Excluded_Cards()
    {
        var result = _intake.Accept(Accept([Line("a"), Line("b", excluded: true)]), null, "clerk");

        Assert.Equal(1, result.CardCount);
        using var db = _factory.CreateDbContext();
        Assert.Single(db.Lots);
        Assert.Single(db.IntakeLines);
    }

    [Fact]
    public void Accept_Trade_Without_Customer_Is_Refused()
    {
        var ex = Assert.Throws<IntakeException>(() => _intake.Accept(Accept([Line("a", payout: "Trade")]), null, "clerk"));
        Assert.Equal(400, ex.StatusCode);
        using var db = _factory.CreateDbContext();
        Assert.Empty(db.Intakes);
        Assert.Empty(db.Lots);
    }

    [Fact]
    public void Accept_Needs_A_Price_For_Every_Included_Card()
    {
        Assert.Throws<IntakeException>(() => _intake.Accept(Accept([Line("x")]), null, "clerk"));

        var result = _intake.Accept(Accept([Line("x", manual: 3m)]), null, "clerk");
        Assert.Equal(2.1m, result.CashPaid);
        using var db = _factory.CreateDbContext();
        Assert.True(db.IntakeLines.Single().IsManualPrice);
    }

    [Fact]
    public void Accept_Refuses_When_Totals_Moved_Since_The_Quote()
    {
        var request = Accept([Line("a")]) with { ExpectedCashPaid = 99m };

        var ex = Assert.Throws<IntakeException>(() => _intake.Accept(request, null, "clerk"));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(7m, ex.Quote?.CashPaid);
    }

    [Fact]
    public void Accept_Without_Intake_Location_Is_Refused()
    {
        _settings.Value.LocationId = null;
        var ex = Assert.Throws<IntakeException>(() => _intake.Accept(Accept([Line("a")]), null, "clerk"));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void History_Lists_Newest_First_And_Returns_Details()
    {
        var first = _intake.Accept(Accept([Line("a")]), null, "clerk");
        var second = _intake.Accept(Accept([Line("b", payout: "Trade")], _customer), null, "clerk");

        var page = _intake.List(0, 50, null, null, null, null);
        Assert.Equal(2, page.Total);
        Assert.Equal(second.IntakeId, page.Items[0].Id);
        Assert.Single(_intake.List(0, 50, _customer, null, null, null).Items);

        var detail = _intake.Get(first.IntakeId)!;
        Assert.Single(detail.Lines);
        Assert.Equal(70m, detail.CashPercent);
        Assert.Equal(70m, detail.Settings?.CashPercent);
    }

    private sealed class FakeSettings : IIntakeSettingsService
    {
        public IntakeSettings Value { get; } = Normalized();
        public IntakeSettings Get() => Value;
        public void Save(IntakeSettings settings) { }

        private static IntakeSettings Normalized()
        {
            var s = new IntakeSettings { BulkThreshold = 0 };
            IntakeSettingsService.Normalize(s);
            return s;
        }
    }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    private sealed class StubDataPath : IDataPathService
    {
        public string DataDirectory => Path.GetTempPath();
        public string ScansDirectory => Path.GetTempPath();
        public string TempScansDirectory => Path.GetTempPath();
        public string SymbolsCacheDirectory => Path.GetTempPath();
        public string LogsDirectory => Path.GetTempPath();
        public string TradesDirectory => Path.GetTempPath();
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
