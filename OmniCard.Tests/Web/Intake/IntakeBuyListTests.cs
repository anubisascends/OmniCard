using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Intake;
using OmniCard.Shared.Storage;
using OmniCard.Tests.Web.BuyLists;
using OmniCard.Web.Services;
using OmniCard.Web.Services.Intake;

namespace OmniCard.Tests.Web.Intake;

/// <summary>Intake with the Buy List: the rate on wanted copies, splitting a partly wanted line, the
/// set-aside location, the count-down and the audit.</summary>
public class IntakeBuyListTests : IDisposable
{
    private readonly BuyListFixture _f = new();
    private readonly Settings _settings = new();
    private readonly IntakeService _intake;
    private readonly int _location;

    // Catalog: card "a" market 10; card "b" market 4.
    public IntakeBuyListTests()
    {
        var game = new Mock<ICardGameService>();
        game.SetupGet(g => g.Game).Returns(CardGame.Mtg);
        game.Setup(g => g.GetCurrentPrices(It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
            .Returns((IEnumerable<string> ids, bool _) =>
                ids.Where(id => id is "a" or "b").ToDictionary(id => id, id => id == "a" ? 10m : 4m));
        _f.CardService.Setup(c => c.GetGameService(CardGame.Mtg)).Returns(game.Object);

        _intake = new IntakeService(_f.Factory, _settings, _f.CardService.Object, _f.Commits, new StoreCreditService(_f.Factory),
            _f.Service, NullLogger<IntakeService>.Instance);
        _location = _f.Box("Intake");
        _settings.Value.LocationId = _location;
    }

    public void Dispose() => _f.Dispose();

    private static IntakeQuoteLineRequest Line(string id, int qty = 1, bool? excluded = null) => new()
    {
        Item = new ScanCommitItem { Game = "Mtg", GameCardId = id, Name = "Card " + id, SetCode = "tst", Quantity = qty },
        Excluded = excluded,
    };

    private void Want(string id, int quantity = 1) => _f.Want("Card " + id, cardId: id, quantity: quantity, pin: true);

    private IntakeQuoteDto Quote(params IntakeQuoteLineRequest[] lines) => _intake.Quote(new IntakeQuoteRequest { Lines = lines });

    private IntakeAcceptRequest AcceptOf(params IntakeQuoteLineRequest[] lines)
    {
        var quote = Quote(lines);
        return new IntakeAcceptRequest { Lines = lines, ExpectedCashPaid = quote.CashPaid, ExpectedTradePaid = quote.TradePaid };
    }

    [Fact]
    public void Quote_Pays_The_Buy_List_Rate_And_Flags_The_Line()
    {
        _settings.Value.BuyListCashPercent = 90m;
        _settings.Value.BuyListTradePercent = 100m;
        Want("a");

        var quote = Quote(Line("a"), Line("b"));

        Assert.Equal(9m, quote.Lines[0].CashUnit);
        Assert.Equal(10m, quote.Lines[0].TradeUnit);
        Assert.Equal(1, quote.Lines[0].BuyListCopies);
        Assert.NotNull(quote.Lines[0].BuyList);
        Assert.Null(quote.Lines[1].BuyList);
        Assert.Equal(11.8m, quote.CashOffer); // 9 + 4 × 70%
        Assert.True(quote.BuyListRateApplied);
    }

    [Fact]
    public void Quote_Without_Buy_List_Rates_Pays_The_Regular_Percentages()
    {
        Want("a");

        var quote = Quote(Line("a"));

        Assert.Equal(7m, quote.CashOffer);
        Assert.NotNull(quote.Lines[0].BuyList);
        Assert.False(quote.BuyListRateApplied);
    }

    [Fact]
    public void Quote_Only_The_Copies_Still_Wanted_Get_The_Rate()
    {
        _settings.Value.BuyListCashPercent = 90m;
        Want("a");

        var quote = Quote(Line("a", qty: 3));

        Assert.Equal(1, quote.Lines[0].BuyListCopies);
        Assert.Equal(23m, quote.CashPaid); // 9 + 2 × 7
    }

    [Fact]
    public void Quote_Wanted_Copies_Go_To_The_First_Lines_Left_In()
    {
        _settings.Value.BuyListCashPercent = 90m;
        Want("a");

        var quote = Quote(Line("a", excluded: true), Line("a"));

        Assert.Equal(0, quote.Lines[0].BuyListCopies);
        Assert.NotNull(quote.Lines[0].BuyList);
        Assert.Equal(1, quote.Lines[1].BuyListCopies);
        Assert.Equal(9m, quote.CashPaid);
    }

    [Fact]
    public void Quote_A_Wanted_Card_Is_Never_Bulk()
    {
        _settings.Value.BulkThreshold = 50m;
        _settings.Value.BulkMode = IntakeBulkMode.Exclude;
        Want("a");

        var quote = Quote(Line("a"), Line("b"));

        Assert.False(quote.Lines[0].IsBulk);
        Assert.False(quote.Lines[0].Excluded);
        Assert.True(quote.Lines[1].Excluded);
    }

    [Fact]
    public void Quote_Different_Buy_List_Rates_Rule_Out_A_Single_Offer()
    {
        _settings.Value.CashPercent = 70m;
        _settings.Value.TradePercent = 70m;
        _settings.Value.BuyListCashPercent = 80m;
        _settings.Value.BuyListTradePercent = 90m;
        Want("a");

        Assert.False(Quote(Line("a")).SingleOffer);
        Assert.True(Quote(Line("b")).SingleOffer);
    }

    [Fact]
    public void Accept_Splits_A_Partly_Wanted_Line_And_Sets_Aside_The_Wanted_Copies()
    {
        _settings.Value.BuyListCashPercent = 90m;
        var setAside = _f.Box("For the owner");
        _f.Service.SetSetAside(CardGame.Mtg, setAside);
        Want("a");

        var result = _intake.Accept(AcceptOf(Line("a", qty: 3)), 1, "Staff");

        Assert.Equal(23m, result.CashPaid);
        Assert.Equal((1, 1, 0), (result.BuyList!.MatchedCopies, result.BuyList.SetAsideCopies, result.BuyList.NotSetAside));
        Assert.Equal(["For the owner"], result.BuyList.SetAsideLocations);

        using var db = _f.Factory.CreateDbContext();
        var wanted = db.Lots.Single(l => l.LocationId == setAside);
        var ordinary = db.Lots.Single(l => l.LocationId == _location);
        Assert.Equal((1, 9m), (wanted.Quantity, wanted.UnitCost));
        Assert.Equal((2, 7m), (ordinary.Quantity, ordinary.UnitCost));

        var lines = db.IntakeLines.OrderBy(l => l.Id).ToList();
        Assert.Equal((true, "For the owner", 1, 9m), (lines[0].OnBuyList, lines[0].SetAsideLocationName, lines[0].Quantity, lines[0].UnitPaid));
        Assert.Equal((false, null, 2, 7m), (lines[1].OnBuyList, lines[1].SetAsideLocationName, lines[1].Quantity, lines[1].UnitPaid));
        Assert.Equal(wanted.Id, lines[0].LotId);
        Assert.Equal(ordinary.Id, lines[1].LotId);

        var entry = db.BuyListEntries.Single();
        Assert.Equal((0, 1), (entry.WantedQuantity, entry.AcquiredQuantity));
    }

    [Fact]
    public void Accept_Without_A_Set_Aside_Location_Keeps_Everything_Together()
    {
        Want("a", quantity: 2);

        var result = _intake.Accept(AcceptOf(Line("a")), 1, "Staff");

        using var db = _f.Factory.CreateDbContext();
        Assert.Equal(_location, db.Lots.Single().LocationId);
        Assert.Equal((1, 0), (result.BuyList!.MatchedCopies, result.BuyList.SetAsideCopies));
        Assert.Equal(1, db.BuyListEntries.Single().WantedQuantity);
    }

    [Fact]
    public void Accept_Keeps_Wanted_Copies_Here_When_The_Set_Aside_Site_Is_Off_Limits()
    {
        var setAside = _f.Box("Elsewhere");
        _f.Service.SetSetAside(CardGame.Mtg, setAside);
        Want("a");

        var result = _intake.Accept(AcceptOf(Line("a")), 1, "Staff", canWriteLocation: id => id != setAside);

        using var db = _f.Factory.CreateDbContext();
        Assert.Equal(_location, db.Lots.Single().LocationId);
        Assert.Equal((0, 1), (result.BuyList!.SetAsideCopies, result.BuyList.NotSetAside));
        Assert.Equal(0, db.BuyListEntries.Single().WantedQuantity);
    }

    [Fact]
    public void Accept_Refuses_When_The_List_Moved_Under_The_Quote()
    {
        _settings.Value.BuyListCashPercent = 90m;
        Want("a");
        var request = AcceptOf(Line("a"));
        _f.Service.Delete(_f.Service.List(CardGame.Mtg, false, null).Items.Single().Id);

        var ex = Assert.Throws<IntakeException>(() => _intake.Accept(request, 1, "Staff"));

        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void Accept_Without_A_Wanted_Card_Reports_No_Buy_List()
    {
        Assert.Null(_intake.Accept(AcceptOf(Line("a")), 1, "Staff").BuyList);
    }

    private sealed class Settings : IIntakeSettingsService
    {
        public IntakeSettings Value { get; } = Normalized();
        public IntakeSettings Get() => Value;
        public void Save(IntakeSettings settings) { }

        private static IntakeSettings Normalized()
        {
            var s = new IntakeSettings { RoundingStep = 0m, BulkThreshold = 0m };
            IntakeSettingsService.Normalize(s);
            return s;
        }
    }
}
