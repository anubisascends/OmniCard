using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class TradeImportServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OmniCardDbContext> _factory;
    private readonly string _tradesDir;

    public TradeImportServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_connection)
            .Options;
        _factory = new TestDbContextFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();

        _tradesDir = Path.Combine(Path.GetTempPath(), "OmniCardTradeTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tradesDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_tradesDir))
            Directory.Delete(_tradesDir, recursive: true);
    }

    private TradeImportService CreateService() =>
        new(_factory, new StubDataPathService(_tradesDir), NullLogger<TradeImportService>.Instance);

    private int SeedLot()
    {
        using var ctx = _factory.CreateDbContext();
        var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = "a", Name = "Test Card" };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = product.Id, Quantity = 1 };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    private string WriteTradeRecord(TradeRecord record, string? photoContents = "fake-photo-bytes")
    {
        var folder = Path.Combine(_tradesDir, record.TradeId.ToString());
        Directory.CreateDirectory(folder);
        if (photoContents is not null && record.PhotoFileName.Length > 0)
            File.WriteAllText(Path.Combine(folder, record.PhotoFileName), photoContents);

        var jsonPath = Path.Combine(folder, "trade.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
        return jsonPath;
    }

    [Fact]
    public void ImportPendingTrades_AppliesTrade_SetsIsTradedAndNote()
    {
        var lotId = SeedLot();
        WriteTradeRecord(new TradeRecord { LotId = lotId, Note = "2x Fire Lotus", PhotoFileName = "photo.jpg" });

        var svc = CreateService();
        var count = svc.ImportPendingTrades();

        Assert.Equal(1, count);
        using var ctx = _factory.CreateDbContext();
        var lot = ctx.Lots.Single(l => l.Id == lotId);
        Assert.True(lot.IsTraded);
        Assert.Equal("2x Fire Lotus", lot.TradeNote);
        Assert.NotNull(lot.TradePhotoPath);
        Assert.EndsWith("photo.jpg", lot.TradePhotoPath);
    }

    [Fact]
    public void ImportPendingTrades_CreatesTradeRow_WithSnapshotFieldsAndOriginalLotId()
    {
        var lotId = SeedLot();
        var tradeId = Guid.NewGuid();
        WriteTradeRecord(new TradeRecord
        {
            TradeId = tradeId,
            LotId = lotId,
            Game = CardGame.Mtg,
            CardName = "Lightning Bolt",
            SetCode = "LEA",
            SetName = "Limited Edition Alpha",
            CollectorNumber = "161",
            Foil = true,
            Note = "2x Fire Lotus",
            PhotoFileName = "photo.jpg",
        });

        CreateService().ImportPendingTrades();

        using var ctx = _factory.CreateDbContext();
        var trade = Assert.Single(ctx.Trades);
        Assert.Equal(tradeId, trade.TradeRecordId);
        Assert.Equal("Lightning Bolt", trade.CardName);
        Assert.Equal("LEA", trade.SetCode);
        Assert.True(trade.Foil);
        Assert.Equal("2x Fire Lotus", trade.Note);
        Assert.Equal(lotId, trade.OriginalLotId);
        Assert.Null(trade.FirstFulfilledAt);
    }

    [Fact]
    public void ImportPendingTrades_AddsInventoryMovement()
    {
        var lotId = SeedLot();
        WriteTradeRecord(new TradeRecord { LotId = lotId, Note = "trade", PhotoFileName = "photo.jpg" });

        CreateService().ImportPendingTrades();

        using var ctx = _factory.CreateDbContext();
        var movement = Assert.Single(ctx.Movements.Where(m => m.LotId == lotId));
        Assert.Equal(MovementType.Trade, movement.Type);
    }

    [Fact]
    public void ImportPendingTrades_MarksProcessedAt_SoSecondRunIsNoOp()
    {
        var lotId = SeedLot();
        var jsonPath = WriteTradeRecord(new TradeRecord { LotId = lotId, Note = "trade", PhotoFileName = "photo.jpg" });

        var svc = CreateService();
        Assert.Equal(1, svc.ImportPendingTrades());
        Assert.Equal(0, svc.ImportPendingTrades());

        var record = JsonSerializer.Deserialize<TradeRecord>(File.ReadAllText(jsonPath));
        Assert.NotNull(record!.ProcessedAt);
    }

    [Fact]
    public void ImportPendingTrades_SkipsRecordAlreadyMarkedProcessed()
    {
        var lotId = SeedLot();
        WriteTradeRecord(new TradeRecord { LotId = lotId, Note = "trade", PhotoFileName = "photo.jpg", ProcessedAt = DateTime.UtcNow });

        var count = CreateService().ImportPendingTrades();

        Assert.Equal(0, count);
        using var ctx = _factory.CreateDbContext();
        Assert.False(ctx.Lots.Single(l => l.Id == lotId).IsTraded);
    }

    [Fact]
    public void ImportPendingTrades_MissingLot_MarksProcessedWithErrorInsteadOfRetrying()
    {
        var jsonPath = WriteTradeRecord(new TradeRecord { LotId = 999, Note = "trade", PhotoFileName = "photo.jpg" });

        var count = CreateService().ImportPendingTrades();

        Assert.Equal(0, count);
        var record = JsonSerializer.Deserialize<TradeRecord>(File.ReadAllText(jsonPath));
        Assert.NotNull(record!.ProcessedAt);
        Assert.NotNull(record.ProcessingError);
    }

    [Fact]
    public void ImportPendingTrades_BackfillsTradeRow_FromLiveLot_ForPreSnapshotRecordAlreadyProcessed()
    {
        // Simulates a trade.json written/applied before TradeRecord had snapshot fields and
        // before the Trade table existed: already ProcessedAt, CardName/etc. all empty, no Trade
        // row — but the lot is still sitting there with IsTraded=true from the old code path.
        var lotId = SeedLot();
        using (var ctx = _factory.CreateDbContext())
        {
            ctx.Lots.Single(l => l.Id == lotId).IsTraded = true;
            ctx.SaveChanges();
        }
        var tradeId = Guid.NewGuid();
        WriteTradeRecord(new TradeRecord
        {
            TradeId = tradeId,
            LotId = lotId,
            Note = "old-format trade",
            PhotoFileName = "photo.jpg",
            ProcessedAt = DateTime.UtcNow,
        });

        var count = CreateService().ImportPendingTrades();

        Assert.Equal(0, count); // backfill isn't a fresh "import"
        using var verifyCtx = _factory.CreateDbContext();
        var trade = Assert.Single(verifyCtx.Trades);
        Assert.Equal(tradeId, trade.TradeRecordId);
        Assert.Equal("Test Card", trade.CardName); // pulled from the live Product, not the (empty) JSON
        Assert.Equal(lotId, trade.OriginalLotId);
    }

    [Fact]
    public void ImportPendingTrades_BackfillDoesNotDuplicate_OnRepeatedRuns()
    {
        var lotId = SeedLot();
        WriteTradeRecord(new TradeRecord
        {
            LotId = lotId,
            Note = "trade",
            PhotoFileName = "photo.jpg",
            ProcessedAt = DateTime.UtcNow,
        });

        var svc = CreateService();
        svc.ImportPendingTrades();
        svc.ImportPendingTrades();

        using var ctx = _factory.CreateDbContext();
        Assert.Single(ctx.Trades);
    }

    [Fact]
    public void ImportPendingTrades_ReturnsZero_WhenTradesDirectoryDoesNotExist()
    {
        Directory.Delete(_tradesDir);
        var count = CreateService().ImportPendingTrades();
        Assert.Equal(0, count);
    }

    private class TestDbContextFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    private class StubDataPathService(string tradesDir) : IDataPathService
    {
        public string DataDirectory => "";
        public string ScansDirectory => "";
        public string TempScansDirectory => "";
        public string SymbolsCacheDirectory => "";
        public string LogsDirectory => "";
        public string TradesDirectory => tradesDir;
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) => throw new NotSupportedException();
        public void CommitMigration() => throw new NotSupportedException();
        public void CancelPendingMigration() => throw new NotSupportedException();
    }
}
