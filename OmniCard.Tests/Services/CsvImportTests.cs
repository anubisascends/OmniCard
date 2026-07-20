using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Models;
using OmniCard.Interfaces;
using OmniCard.Collection;

namespace OmniCard.Tests.Services;

public class CsvImportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;

    public CsvImportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbFactory = new TestOmniDbFactory(options);
        using var ctx = _dbFactory.CreateDbContext();
        ctx.Database.EnsureCreated();

        // Seed Bulk container for imports
        ctx.StorageContainers.Add(new StorageContainer
        {
            Name = "Bulk",
            ContainerType = ContainerType.Bulk,
            IsSystem = true,
            SortOrder = 0,
        });
        ctx.SaveChanges();
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private CsvExportImportService CreateService(IStorageContainerService? containerService = null)
    {
        return new CsvExportImportService(
            CreateCardService(),
            null!, // IScryfallService — not needed for app-native import tests
            containerService ?? new StubContainerService(),
            NullLogger<CsvExportImportService>.Instance);
    }

    private CardService CreateCardService() => new(
        new StubHashService(),
        [],
        _dbFactory,
        new StubOcrService(),
        new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
        NullLogger<CardService>.Instance,
        new DataPathService(Path.GetTempPath()),
        new NullScanDiagnosticService(),
        new NullAuditService());

    private string WriteCsv(string filename, string content)
    {
        var path = Path.Combine(_tempDir, filename);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void PreviewImport_DetectsAppNativeFormat()
    {
        var path = WriteCsv("native.csv",
            "Game,GameCardId,Name,SetName,SetCode,Number,Rarity,Condition,IsFoil,PurchasePrice,DateAdded,ContainerName,ContainerType,Page,Slot,Section\n" +
            "Mtg,abc-123,Lightning Bolt,Alpha,LEA,161,common,NM,False,5.99,2026-01-15T00:00:00.0000000Z,,,,\n");

        var preview = CreateService().PreviewImport(path);
        Assert.Equal(CsvFormat.AppNative, preview.DetectedFormat);
        Assert.Single(preview.Cards);
        Assert.Equal("Lightning Bolt", preview.Cards[0].Name);
        Assert.Equal(CardGame.Mtg, preview.Cards[0].Game);
    }

    [Fact]
    public void PreviewImport_DetectsTcgPlayerFormat()
    {
        var path = WriteCsv("tcg.csv",
            "Quantity,Name,Set Name,Number,Condition,Printing,Price\n" +
            "1,Lightning Bolt,Alpha,161,Near Mint,Normal,5.99\n");

        var preview = CreateService().PreviewImport(path);
        Assert.Equal(CsvFormat.TcgPlayer, preview.DetectedFormat);
    }

    [Fact]
    public void PreviewImport_DetectsMoxfieldFormat()
    {
        var path = WriteCsv("mox.csv",
            "Count,Name,Edition,Collector Number,Condition,Foil,Purchase Price\n" +
            "1,Lightning Bolt,LEA,161,NM,,5.99\n");

        var preview = CreateService().PreviewImport(path);
        Assert.Equal(CsvFormat.Moxfield, preview.DetectedFormat);
    }

    [Fact]
    public void PreviewImport_DetectsManaboxFormat()
    {
        var csv = "Name,Set code,Set name,Collector number,Foil,Rarity,Quantity,Scryfall ID,Purchase price,Misprint,Altered,Condition,Language,Purchase price currency,Added\n"
                + "Lightning Bolt,lea,Alpha,1,normal,common,1,abc-123,5.99,false,false,near_mint,en,USD,2026-01-01T00:00:00.0000000Z\n";
        var path = WriteCsv("manabox.csv", csv);
        var svc = CreateService();

        var preview = svc.PreviewImport(path);

        Assert.Equal(CsvFormat.Manabox, preview.DetectedFormat);
        Assert.Single(preview.Cards);
        Assert.Equal("abc-123", preview.Cards[0].GameCardId);
        Assert.Equal("NM", preview.Cards[0].Condition);
    }

    [Fact]
    public void PreviewImport_AppNative_ParsesLocationData()
    {
        var path = WriteCsv("loc.csv",
            "Game,GameCardId,Name,SetName,SetCode,Number,Rarity,Condition,IsFoil,PurchasePrice,DateAdded,ContainerName,ContainerType,Page,Slot,Section\n" +
            "Mtg,abc-123,Lightning Bolt,Alpha,LEA,161,common,NM,False,,2026-01-15T00:00:00.0000000Z,Red Binder,Binder,3,7,\n");

        var preview = CreateService().PreviewImport(path);
        Assert.Equal("Red Binder", preview.Cards[0].Container?.Name);
        Assert.Equal(3, preview.Cards[0].Page);
        Assert.Equal(7, preview.Cards[0].Slot);
    }

    [Fact]
    public void ImportCards_AddsCardsToDatabase()
    {
        var path = WriteCsv("import.csv",
            "Game,GameCardId,Name,SetName,SetCode,Number,Rarity,Condition,IsFoil,PurchasePrice,DateAdded,ContainerName,ContainerType,Page,Slot,Section\n" +
            "Mtg,abc-123,Lightning Bolt,Alpha,LEA,161,common,NM,False,5.99,2026-01-15T00:00:00.0000000Z,,,,\n");

        var svc = CreateService();
        var preview = svc.PreviewImport(path);
        var count = svc.ImportCards(preview, skipDuplicates: false);

        Assert.Equal(1, count);

        using var ctx = _dbFactory.CreateDbContext();
        var lot = ctx.Lots.AsNoTracking().Include(l => l.Product).First();
        Assert.Equal("Lightning Bolt", lot.Product.Name);
        Assert.Equal("abc-123", lot.Product.GameCardId);
        Assert.Equal(5.99m, lot.UnitCost);
    }

    [Fact]
    public void ImportCards_SkipsDuplicates()
    {
        // Seed a card (Product + Lot)
        using (var ctx = _dbFactory.CreateDbContext())
        {
            SeedExisting(ctx);
        }

        var path = WriteCsv("dup.csv",
            "Game,GameCardId,Name,SetName,SetCode,Number,Rarity,Condition,IsFoil,PurchasePrice,DateAdded,ContainerName,ContainerType,Page,Slot,Section\n" +
            "Mtg,abc-123,Lightning Bolt,Alpha,LEA,161,common,NM,False,,,,,,,\n");

        var svc = CreateService();
        var preview = svc.PreviewImport(path);
        var count = svc.ImportCards(preview, skipDuplicates: true);

        Assert.Equal(0, count);
    }

    [Fact]
    public void ImportCards_AddsDuplicatesWhenNotSkipping()
    {
        // Seed a card (Product + Lot)
        using (var ctx = _dbFactory.CreateDbContext())
        {
            SeedExisting(ctx);
        }

        var path = WriteCsv("dup2.csv",
            "Game,GameCardId,Name,SetName,SetCode,Number,Rarity,Condition,IsFoil,PurchasePrice,DateAdded,ContainerName,ContainerType,Page,Slot,Section\n" +
            "Mtg,abc-123,Lightning Bolt,Alpha,LEA,161,common,NM,False,,,,,,,\n");

        var svc = CreateService();
        var preview = svc.PreviewImport(path);
        var count = svc.ImportCards(preview, skipDuplicates: false);

        Assert.Equal(1, count);

        using var ctx2 = _dbFactory.CreateDbContext();
        Assert.Equal(2, ctx2.Lots.Count());
    }

    private static void SeedExisting(OmniCardDbContext ctx)
    {
        var product = new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Single,
            GameCardId = "abc-123",
            Name = "Lightning Bolt",
            SetName = "Alpha",
            SetCode = "LEA",
            CollectorNumber = "161",
            Rarity = "common",
            Foil = false,
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        ctx.Lots.Add(new InventoryLot { ProductId = product.Id, Condition = "NM" });
        ctx.SaveChanges();
    }

    [Fact]
    public void PreviewImport_ManaboxFoil_MapsFoilCorrectly()
    {
        var csv = "Name,Set code,Set name,Collector number,Foil,Rarity,Quantity,Scryfall ID,Purchase price,Misprint,Altered,Condition,Language,Purchase price currency,Added\n"
                + "Lightning Bolt,lea,Alpha,1,foil,common,1,abc-123,,false,false,near_mint,en,USD,2026-01-01T00:00:00.0000000Z\n";
        var path = WriteCsv("foil.csv", csv);
        var svc = CreateService();

        var preview = svc.PreviewImport(path);

        Assert.True(preview.Cards[0].IsFoil);
    }

    [Fact]
    public void PreviewImport_TcgPlayer_MapsConditionCorrectly()
    {
        var path = WriteCsv("tcg_cond.csv",
            "Quantity,Name,Set Name,Number,Condition,Printing,Price\n" +
            "1,Lightning Bolt,Alpha,161,Lightly Played,Foil,3.50\n");

        var preview = CreateService().PreviewImport(path);
        Assert.Single(preview.Cards);
        Assert.Equal("LP", preview.Cards[0].Condition);
        Assert.True(preview.Cards[0].IsFoil);
        Assert.Equal(3.50m, preview.Cards[0].PurchasePrice);
    }

    private class TestOmniDbFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    private class StubHashService : IPerceptualHashService
    {
        public ulong ComputeHash(Stream imageStream, Action<HashStageResult>? onStage = null) => 0;
        public ulong ComputeEdgeHash(Stream imageStream, Action<HashStageResult>? onStage = null) => 0;
        public ulong[] ComputeArtHash(Stream imageStream, (double X, double Y, double W, double H)[] cropRegions, Action<HashStageResult>? onStage = null) => new ulong[cropRegions.Length];
    }

    private class StubOcrService : IOcrMatchingService
    {
        public Dictionary<string, ulong> SymbolHashes { get; set; } = [];
        public Task<OcrMatchResult> AnalyzeCardAsync(byte[] imageData) => Task.FromResult(new OcrMatchResult());
        public (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] imageData) => ([], 0);
        public Task<(string? CollectorNumber, double Confidence)> DetectOptcgCollectorNumberAsync(byte[] imageData) => Task.FromResult<(string?, double)>((null, 0));
    }

    private class NullScanDiagnosticService : IScanDiagnosticService
    {
        public void LogScanCompleted(string sessionId, ulong scanHash, CardMatch? match, MatchDiagnostics? diagnostics, ulong[]? artHashes, OcrMatchResult? ocrResult, FlagReason autoFlagReason) { }
        public void LogUserFlagged(ulong scanHash, ScannedCard card) { }
        public void LogUserConfirmed(ulong scanHash, ScannedCard card) { }
        public void LogUserCorrected(ulong scanHash, ScannedCard card, CardMatch newMatch) { }
        public void LogUserUnflagged(ulong scanHash, ScannedCard card, FlagReason previousReason) { }
        public void ExportDiagnostics(string filePath) { }
        public void ClearDiagnostics() { }
        public int GetEventCount() => 0;
    }

    private class NullAuditService : IAuditService
    {
        public bool IsAuditActive => false;
        public int? AuditLocationId => null;
        public string? AuditLocationName => null;
        public void StartAudit(int containerId) { }
        public void EndAudit() { }
        public CardMatch? FindScopedMatch(ulong hash, ulong[]? artHashes) => null;
        public AuditReport GenerateReport(IEnumerable<ScannedCard> scannedCards) => throw new NotImplementedException();
    }

    private class StubContainerService : IStorageContainerService
    {
        private readonly List<StorageContainer> _containers =
        [
            new() { Id = 1, Name = "Bulk", ContainerType = ContainerType.Bulk, IsSystem = true, SortOrder = 0 }
        ];
        private int _nextId = 2;

        public List<StorageContainer> GetAll() => _containers;
        public StorageContainer GetBulk() => _containers.First(c => c.IsSystem);
        public StorageContainer Create(string name, ContainerType type)
        {
            var c = new StorageContainer { Id = _nextId++, Name = name, ContainerType = type };
            _containers.Add(c);
            return c;
        }
        public void Rename(int id, string newName) { }
        public void Delete(int id, bool moveCardsToBulk = true) { }
        public int GetCardCount(int containerId) => 0;
        public void SetCoverCard(int containerId, int? cardId) { }
        public List<CollectionCard> GetCardsInContainer(int containerId) => [];
        public void SetExcludeFromDeckCheck(int containerId, bool exclude) { }
    }
}
