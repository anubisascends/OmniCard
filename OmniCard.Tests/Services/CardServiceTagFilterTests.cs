using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Models;
using OmniCard.Interfaces;
using OmniCard.Collection;

namespace OmniCard.Tests.Services;

public class CardServiceTagFilterTests : IDisposable
{
    private readonly SqliteConnection _omniConnection;
    private readonly DbContextOptions<OmniCardDbContext> _omniOptions;

    public CardServiceTagFilterTests()
    {
        _omniConnection = new SqliteConnection("Data Source=:memory:");
        _omniConnection.Open();
        _omniOptions = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_omniConnection)
            .Options;
        using var omniCtx = new OmniCardDbContext(_omniOptions);
        omniCtx.Database.EnsureCreated();
        SeedCards(omniCtx);
    }

    public void Dispose() => _omniConnection.Dispose();

    // Lot ids assigned by seed order: 1=Wrath of God (foil, psa), 2=Counterspell (foil),
    // 3=Dark Ritual (trade), 4=Lightning Bolt (untagged)
    private static void SeedCards(OmniCardDbContext ctx)
    {
        var wrath = SeedCard(ctx, "1", "Wrath of God", "lea", "Alpha");
        var counterspell = SeedCard(ctx, "2", "Counterspell", "lea", "Alpha");
        var ritual = SeedCard(ctx, "3", "Dark Ritual", "lea", "Alpha");
        SeedCard(ctx, "4", "Lightning Bolt", "lea", "Alpha");

        var foilTag = new Tag { Name = "Foil" };
        var psaTag = new Tag { Name = "PSA-Worthy" };
        var tradeTag = new Tag { Name = "Trade Bait" };
        ctx.Tags.AddRange(foilTag, psaTag, tradeTag);
        ctx.SaveChanges();

        ctx.LotTags.AddRange(
            new LotTag { LotId = wrath.Id, TagId = foilTag.Id },
            new LotTag { LotId = wrath.Id, TagId = psaTag.Id },
            new LotTag { LotId = counterspell.Id, TagId = foilTag.Id },
            new LotTag { LotId = ritual.Id, TagId = tradeTag.Id });
        ctx.SaveChanges();
    }

    private static InventoryLot SeedCard(OmniCardDbContext ctx, string gameCardId, string name, string setCode, string setName)
    {
        var product = new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Single,
            GameCardId = gameCardId,
            Name = name,
            SetCode = setCode,
            SetName = setName,
            Rarity = "Common",
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();

        var lot = new InventoryLot { ProductId = product.Id };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot;
    }

    private IDbContextFactory<OmniCardDbContext> CreateOmniFactory() => new MockOmniFactory(_omniOptions);

    private CardService CreateService() => new(
        new StubHashService(),
        [],
        CreateOmniFactory(),
        new StubOcrService(),
        new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
        NullLogger<CardService>.Instance,
        new DataPathService(Path.GetTempPath()),
        new NullScanDiagnosticService(),
        new NullAuditService(),
        new StubScannerSettingsService());

    [Fact]
    public void SearchCollection_TagFilter_MatchesOnlyTaggedLot()
    {
        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("tag:foil", CardGame.Mtg, null, null, null, results);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, c => c.Name == "Wrath of God");
        Assert.Contains(results, c => c.Name == "Counterspell");
    }

    [Fact]
    public void SearchCollection_TagFilter_IsPartialMatch()
    {
        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("tag:psa", CardGame.Mtg, null, null, null, results);

        Assert.Single(results);
        Assert.Equal("Wrath of God", results[0].Name);
    }

    [Fact]
    public void SearchCollection_TagFilter_Negation_ExcludesTaggedLots()
    {
        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("-tag:foil", CardGame.Mtg, null, null, null, results);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, c => c.Name == "Dark Ritual");
        Assert.Contains(results, c => c.Name == "Lightning Bolt");
    }

    [Fact]
    public void SearchCollection_TagFilter_OrAcrossTwoTags()
    {
        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("tag:psa OR tag:trade", CardGame.Mtg, null, null, null, results);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, c => c.Name == "Wrath of God");
        Assert.Contains(results, c => c.Name == "Dark Ritual");
    }

    [Fact]
    public void SearchCollection_TagFilter_NoMatch_ReturnsEmpty()
    {
        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("tag:nonexistent", CardGame.Mtg, null, null, null, results);

        Assert.Empty(results);
    }

    // --- Stubs (same as CollectionSortFilterTests) ---
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
        public Task<(string? CollectorNumber, double Confidence)> DetectRiftboundCollectorNumberAsync(byte[] imageData) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? CollectorNumber, double Confidence)> DetectCollectorNumberAsync(byte[] imageData, OcrCollectorSpec spec) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? SetCode, string? CollectorNumber, double Confidence)> DetectMtgSetAndNumberAsync(byte[] imageData) => Task.FromResult<(string?, string?, double)>((null, null, 0));
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

    private class MockOmniFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
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
        public AuditReport GenerateFileAuditReport(int containerId, IEnumerable<CollectionCard> importedCards) => throw new NotImplementedException();
    }

    private class StubScannerSettingsService : IScannerSettingsService
    {
        public ScanWorkflowMode WorkflowMode { get; private set; } = ScanWorkflowMode.Store;
        public void SetWorkflowMode(ScanWorkflowMode mode) => WorkflowMode = mode;
    }
}
