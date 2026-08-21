using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class TradeServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OmniCardDbContext> _factory;

    public TradeServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_connection)
            .Options;
        _factory = new TestDbContextFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private TradeService CreateService() => new(_factory, new StubDataPathService());

    private int SeedSession(DateTime createdAt, decimal outgoingValue = 0m, decimal? receivedValue = null, params string[] cardNames)
    {
        using var ctx = _factory.CreateDbContext();
        var session = new TradeSession
        {
            SessionRecordId = Guid.NewGuid(),
            Note = "traded",
            OutgoingValue = outgoingValue,
            ReceivedValue = receivedValue,
            CreatedAt = createdAt,
            ImportedAt = createdAt,
        };
        ctx.TradeSessions.Add(session);
        ctx.SaveChanges();

        foreach (var name in cardNames.DefaultIfEmpty("Card"))
        {
            ctx.Trades.Add(new Trade
            {
                TradeSessionId = session.Id,
                TradeRecordId = session.SessionRecordId,
                Game = CardGame.Mtg,
                CardName = name,
                OriginalLotId = 1,
                CreatedAt = createdAt,
                ImportedAt = createdAt,
            });
        }
        ctx.SaveChanges();
        return session.Id;
    }

    private void SeedReplacementLot(int sessionId)
    {
        using var ctx = _factory.CreateDbContext();
        var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = Guid.NewGuid().ToString(), Name = "Replacement" };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        ctx.Lots.Add(new InventoryLot { ProductId = product.Id, FulfilledTradeSessionId = sessionId });
        ctx.SaveChanges();
    }

    [Fact]
    public void GetTrades_ReturnsNewestFirst()
    {
        SeedSession(DateTime.UtcNow.AddDays(-1), cardNames: "Older");
        SeedSession(DateTime.UtcNow, cardNames: "Newer");

        var trades = CreateService().GetTrades();

        Assert.Equal(["Newer", "Older"], trades.Select(t => t.OutgoingCards[0].CardName));
    }

    [Fact]
    public void GetTrades_IncludesAllOutgoingCards()
    {
        SeedSession(DateTime.UtcNow, cardNames: ["Sol Ring", "Mana Crypt", "Mox Opal"]);

        var trade = Assert.Single(CreateService().GetTrades());

        Assert.Equal(3, trade.OutgoingCards.Count);
        Assert.Equal("Sol Ring (+2 more)", trade.Label);
    }

    [Fact]
    public void GetTrades_ComputesReplacementCount_FromSessionLink()
    {
        var sessionId = SeedSession(DateTime.UtcNow, cardNames: "Traded Card");
        SeedReplacementLot(sessionId);
        SeedReplacementLot(sessionId);

        var trade = Assert.Single(CreateService().GetTrades());

        Assert.Equal(2, trade.ReplacementCount);
    }

    [Fact]
    public void GetTrades_ComputesValueDelta()
    {
        SeedSession(DateTime.UtcNow, outgoingValue: 10m, receivedValue: 14m, cardNames: "Traded Card");

        var trade = Assert.Single(CreateService().GetTrades());

        Assert.Equal(10m, trade.OutgoingValue);
        Assert.Equal(14m, trade.ReceivedValue);
        Assert.Equal(4m, trade.ValueDelta);
    }

    [Fact]
    public void GetTrades_ValueDeltaNull_WhenReceivedValueUnknown()
    {
        SeedSession(DateTime.UtcNow, outgoingValue: 10m, cardNames: "Traded Card");

        var trade = Assert.Single(CreateService().GetTrades());

        Assert.Null(trade.ValueDelta);
    }

    [Fact]
    public void GetTrade_ReturnsMatchingSummary()
    {
        var sessionId = SeedSession(DateTime.UtcNow, cardNames: "Traded Card");

        var summary = CreateService().GetTrade(sessionId);

        Assert.NotNull(summary);
        Assert.Equal("Traded Card", summary.OutgoingCards[0].CardName);
    }

    [Fact]
    public void GetTrade_ReturnsNull_WhenNotFound()
    {
        Assert.Null(CreateService().GetTrade(999));
    }

    private class TestDbContextFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    private class StubDataPathService : IDataPathService
    {
        public string DataDirectory => "";
        public string ScansDirectory => "";
        public string TempScansDirectory => "";
        public string SymbolsCacheDirectory => "";
        public string LogsDirectory => "";
        public string TradesDirectory => "";
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) => throw new NotSupportedException();
        public void CommitMigration() => throw new NotSupportedException();
        public void CancelPendingMigration() => throw new NotSupportedException();
    }
}
