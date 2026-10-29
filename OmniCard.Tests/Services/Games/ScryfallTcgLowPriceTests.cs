using Microsoft.EntityFrameworkCore;
using OmniCard.CardMatching.Games;
using OmniCard.Data.Catalogs;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;

namespace OmniCard.Tests.Services.Games;

/// <summary>MTG low prices: Scryfall has none, so the price refresh copies TCGCSV's lows onto printings
/// by TcgplayerId — without loading or clobbering the rest of the (large) card rows.</summary>
public class ScryfallTcgLowPriceTests : IDisposable
{
    private readonly ScryfallDbContext _context;

    public ScryfallTcgLowPriceTests()
    {
        var options = new DbContextOptionsBuilder<ScryfallDbContext>().UseSqlite("Data Source=:memory:").Options;
        _context = new ScryfallDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    private Guid AddCard(int? tcgplayerId, string usd = "9.99")
    {
        var id = Guid.NewGuid();
        _context.Cards.Add(new Card
        {
            Id = id, Name = "Card " + tcgplayerId, Lang = "en", SetCode = "tst", CollectorNumber = "1",
            TcgplayerId = tcgplayerId, Prices = new Prices { Usd = usd },
        });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
        return id;
    }

    private static List<TcgCsvPrice> Rows(int productId, decimal? low, decimal? foilLow) =>
    [
        new() { ProductId = productId, SubTypeName = "Normal", LowPrice = low, MarketPrice = 1m },
        new() { ProductId = productId, SubTypeName = "Foil", LowPrice = foilLow, MarketPrice = 2m },
    ];

    [Fact]
    public void Writes_Normal_And_Foil_Lows_By_TcgplayerId()
    {
        var a = AddCard(100);
        var b = AddCard(200);
        var unlinked = AddCard(null);

        var changed = ScryfallService.ApplyTcgLowPrices(_context, new Dictionary<int, List<TcgCsvPrice>>
        {
            [100] = Rows(100, 0.50m, 1.25m),
            [999] = Rows(999, 5m, 5m),
        });

        Assert.Equal(1, changed);
        var cards = _context.Cards.AsNoTracking().ToDictionary(c => c.Id);
        Assert.Equal(0.50m, cards[a].TcgLowPrice);
        Assert.Equal(1.25m, cards[a].TcgFoilLowPrice);
        Assert.Null(cards[b].TcgLowPrice);
        Assert.Null(cards[unlinked].TcgLowPrice);
        // The stub update touched only the low columns.
        Assert.Equal("9.99", cards[a].Prices?.Usd);
        Assert.Equal("Card 100", cards[a].Name);
    }

    [Fact]
    public void Unchanged_Lows_Are_Not_Rewritten()
    {
        AddCard(100);
        var rows = new Dictionary<int, List<TcgCsvPrice>> { [100] = Rows(100, 0.50m, null) };

        Assert.Equal(1, ScryfallService.ApplyTcgLowPrices(_context, rows));
        Assert.Equal(0, ScryfallService.ApplyTcgLowPrices(_context, rows));
    }
}
