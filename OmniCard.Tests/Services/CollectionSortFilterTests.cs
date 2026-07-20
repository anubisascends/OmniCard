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

public class CollectionSortFilterTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CollectionDbContext> _options;
    private readonly SqliteConnection _omniConnection;
    private readonly DbContextOptions<OmniCardDbContext> _omniOptions;

    public CollectionSortFilterTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new CollectionDbContext(_options);
        ctx.Database.EnsureCreated();

        _omniConnection = new SqliteConnection("Data Source=:memory:");
        _omniConnection.Open();
        _omniOptions = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_omniConnection)
            .Options;
        using var omniCtx = new OmniCardDbContext(_omniOptions);
        omniCtx.Database.EnsureCreated();
        SeedCards(omniCtx);
    }

    public void Dispose()
    {
        _connection.Dispose();
        _omniConnection.Dispose();
    }

    private static void SeedCards(OmniCardDbContext ctx)
    {
        SeedCard(ctx, "1", "Wrath of God", "W", "Sorcery", "lea", "Alpha", "Rare");
        SeedCard(ctx, "2", "Counterspell", "U", "Instant", "lea", "Alpha", "Uncommon");
        SeedCard(ctx, "3", "Dark Ritual", "B", "Instant", "lea", "Alpha", "Common");
        SeedCard(ctx, "4", "Lightning Bolt", "R", "Instant", "lea", "Alpha", "Common");
        SeedCard(ctx, "5", "Llanowar Elves", "G", "Creature", "lea", "Alpha", "Common");
        SeedCard(ctx, "6", "Sol Ring", "Colorless", "Artifact", "lea", "Alpha", "Uncommon");
        SeedCard(ctx, "7", "Azorius Charm", "WU", "Instant", "rtr", "RTR", "Uncommon");
        SeedCard(ctx, "8", "Unknown Card", null, null, "", "", "", flagReason: FlagReason.MissingFromDatabase);
    }

    private static void SeedCard(OmniCardDbContext ctx, string gameCardId, string name, string? color, string? cardType,
        string setCode, string setName, string rarity, FlagReason? flagReason = null)
    {
        var product = new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Single,
            GameCardId = gameCardId,
            Name = name,
            Color = color,
            CardType = cardType,
            SetCode = setCode,
            SetName = setName,
            Rarity = rarity,
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();

        ctx.Lots.Add(new InventoryLot { ProductId = product.Id, FlagReason = flagReason });
        ctx.SaveChanges();
    }

    private IDbContextFactory<CollectionDbContext> CreateFactory() => new MockFactory(_options);
    private IDbContextFactory<OmniCardDbContext> CreateOmniFactory() => new MockOmniFactory(_omniOptions);

    private CardService CreateService() => new(
        new StubHashService(),
        [],
        CreateFactory(),
        CreateOmniFactory(),
        new StubOcrService(),
        new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
        NullLogger<CardService>.Instance,
        new DataPathService(Path.GetTempPath()),
        new NullScanDiagnosticService(),
        new NullAuditService());

    [Fact]
    public void SearchCollection_WithSortPreset_CustomColorOrder()
    {
        var sort = new SortPreset
        {
            Name = "Color Sort",
            Game = CardGame.Mtg,
            SortLevels =
            [
                new SortLevel
                {
                    Field = "Color",
                    Direction = SortDirection.Ascending,
                    CustomOrder = ["W", "U", "B", "R", "G", "Multi", "Land", "Colorless"]
                }
            ]
        };

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("", CardGame.Mtg, null, sort, null, results);

        Assert.Equal("W", results[0].Color);
        Assert.Equal("U", results[1].Color);
        Assert.Equal("B", results[2].Color);
        Assert.Equal("R", results[3].Color);
        Assert.Equal("G", results[4].Color);
        // "WU" doesn't match any entry exactly → treated as "Multi"
        Assert.Equal("WU", results[5].Color);
        Assert.Equal("Colorless", results[6].Color);
    }

    [Fact]
    public void SearchCollection_WithSortPreset_MultiLevel()
    {
        var sort = new SortPreset
        {
            Name = "Color then Name",
            Game = CardGame.Mtg,
            SortLevels =
            [
                new SortLevel
                {
                    Field = "Color",
                    Direction = SortDirection.Ascending,
                    CustomOrder = ["W", "U", "B", "R", "G"]
                },
                new SortLevel { Field = "Name", Direction = SortDirection.Ascending }
            ]
        };

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("", CardGame.Mtg, null, sort, null, results);

        // W comes first
        Assert.Equal("Wrath of God", results[0].Name);
        // U: Azorius (WU, not matched) vs Counterspell (U, matched)
        Assert.Equal("Counterspell", results[1].Name);
    }

    [Fact]
    public void SearchCollection_WithSortPreset_DescendingDirection()
    {
        var sort = new SortPreset
        {
            Name = "Name Desc",
            Game = CardGame.Mtg,
            SortLevels =
            [
                new SortLevel { Field = "Name", Direction = SortDirection.Descending }
            ]
        };

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("", CardGame.Mtg, null, sort, null, results);

        Assert.Equal("Wrath of God", results[0].Name);
        Assert.Equal("Unknown Card", results[1].Name);
    }

    [Fact]
    public void SearchCollection_NoPreset_FallsBackToNameSort()
    {
        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("", CardGame.Mtg, null, null, null, results);

        Assert.Equal("Azorius Charm", results[0].Name);
        Assert.Equal("Counterspell", results[1].Name);
    }

    [Fact]
    public void SearchCollection_ScryfallSyntax_ColorFilter()
    {
        var results = new ObservableCollection<CollectionCard>();
        // c:w is inclusive — matches any card containing white (W, WU, WR, etc.)
        CreateService().SearchCollection("c:w", CardGame.Mtg, null, null, null, results);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, c => c.Name == "Wrath of God");
        Assert.Contains(results, c => c.Name == "Azorius Charm");
    }

    [Fact]
    public void SearchCollection_ScryfallSyntax_TypeFilter()
    {
        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("t:instant", CardGame.Mtg, null, null, null, results);

        Assert.All(results, c => Assert.Equal("Instant", c.CardType));
    }

    [Fact]
    public void SearchCollection_ScryfallSyntax_SetFilter()
    {
        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("set:rtr", CardGame.Mtg, null, null, null, results);

        Assert.Single(results);
        Assert.Equal("Azorius Charm", results[0].Name);
    }

    [Fact]
    public void SearchCollection_ScryfallSyntax_RarityFilter()
    {
        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("r:common", CardGame.Mtg, null, null, null, results);

        Assert.Equal(3, results.Count);
        Assert.All(results, c => Assert.Equal("Common", c.Rarity));
    }

    [Fact]
    public void SearchCollection_ScryfallSyntax_CombinedFilters()
    {
        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("t:instant r:common", CardGame.Mtg, null, null, null, results);

        Assert.Equal(2, results.Count);
        Assert.All(results, c => { Assert.Equal("Instant", c.CardType); Assert.Equal("Common", c.Rarity); });
    }

    [Fact]
    public void SearchCollection_ScryfallSyntax_NameSearch()
    {
        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("lightning", CardGame.Mtg, null, null, null, results);

        Assert.Single(results);
        Assert.Equal("Lightning Bolt", results[0].Name);
    }

    [Fact]
    public void SearchCollection_ScryfallSyntax_QuotedName()
    {
        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("\"wrath of god\"", CardGame.Mtg, null, null, null, results);

        Assert.Single(results);
        Assert.Equal("Wrath of God", results[0].Name);
    }

    [Fact]
    public void SearchCollection_FilterPresetWithQuery()
    {
        // c:u is inclusive — matches any card containing blue (U, WU, etc.)
        var filter = new FilterPreset { Name = "Blue Cards", Game = CardGame.Mtg, Query = "c:u" };

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("", CardGame.Mtg, null, null, filter, results);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, c => c.Name == "Counterspell");
        Assert.Contains(results, c => c.Name == "Azorius Charm");
    }

    [Fact]
    public void SearchCollection_QueryAndFilterPresetCombined()
    {
        var filter = new FilterPreset { Name = "Instants", Game = CardGame.Mtg, Query = "t:instant" };

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("r:common", CardGame.Mtg, null, null, filter, results);

        // Common instants: Dark Ritual, Lightning Bolt
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void SearchCollection_IsMissingDb_FiltersToMissingFromDatabase()
    {
        var results = new ObservableCollection<CollectionCard>();
        var filter = new FilterPreset { Name = "Missing", Game = CardGame.Mtg, Query = "is:missingdb" };
        CreateService().SearchCollection("", CardGame.Mtg, null, null, filter, results);

        Assert.Single(results);
        Assert.Equal("Unknown Card", results[0].Name);
        Assert.Equal(FlagReason.MissingFromDatabase, results[0].FlagReason);
    }

    // --- Stubs (same as CardServiceCollectionTests) ---
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

    private class MockFactory(DbContextOptions<CollectionDbContext> options) : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
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
    }
}
