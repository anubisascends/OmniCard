using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
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

    private TradeService CreateService() => new(_factory);

    private int SeedTrade(string cardName, DateTime createdAt, int? originalLotId = 1)
    {
        using var ctx = _factory.CreateDbContext();
        var trade = new Trade
        {
            TradeRecordId = Guid.NewGuid(),
            Game = CardGame.Mtg,
            CardName = cardName,
            Note = "traded",
            OriginalLotId = originalLotId,
            CreatedAt = createdAt,
            ImportedAt = createdAt,
        };
        ctx.Trades.Add(trade);
        ctx.SaveChanges();
        return trade.Id;
    }

    private void SeedReplacementLot(int tradeId)
    {
        using var ctx = _factory.CreateDbContext();
        var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = "r", Name = "Replacement" };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        ctx.Lots.Add(new InventoryLot { ProductId = product.Id, FulfilledTradeId = tradeId });
        ctx.SaveChanges();
    }

    [Fact]
    public void GetTrades_ReturnsNewestFirst()
    {
        SeedTrade("Older", DateTime.UtcNow.AddDays(-1));
        SeedTrade("Newer", DateTime.UtcNow);

        var trades = CreateService().GetTrades();

        Assert.Equal(["Newer", "Older"], trades.Select(t => t.CardName));
    }

    [Fact]
    public void GetTrades_ComputesReplacementCount()
    {
        var tradeId = SeedTrade("Traded Card", DateTime.UtcNow);
        SeedReplacementLot(tradeId);
        SeedReplacementLot(tradeId);

        var trade = Assert.Single(CreateService().GetTrades());

        Assert.Equal(2, trade.ReplacementCount);
    }

    [Fact]
    public void GetTrades_ZeroReplacements_WhenNoneLinked()
    {
        SeedTrade("Traded Card", DateTime.UtcNow);

        var trade = Assert.Single(CreateService().GetTrades());

        Assert.Equal(0, trade.ReplacementCount);
    }

    [Fact]
    public void GetTrade_ReturnsMatchingSummary()
    {
        var tradeId = SeedTrade("Traded Card", DateTime.UtcNow);

        var summary = CreateService().GetTrade(tradeId);

        Assert.NotNull(summary);
        Assert.Equal("Traded Card", summary.CardName);
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
}
