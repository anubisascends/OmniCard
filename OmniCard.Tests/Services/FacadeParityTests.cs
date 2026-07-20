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

/// <summary>
/// Phase 2a Task 3 safety net: proves the CardService read facade — now sourced from
/// OmniCardDbContext's Lots⋈Products instead of the Phase-1 CollectionDbContext.Cards table —
/// reproduces the pre-migration stacking/sort/filter/pagination behavior exactly.
/// </summary>
public class FacadeParityTests : IDisposable
{
    private readonly SqliteConnection _omniConnection;
    private readonly DbContextOptions<OmniCardDbContext> _omniOptions;

    public FacadeParityTests()
    {
        _omniConnection = new SqliteConnection("Data Source=:memory:");
        _omniConnection.Open();
        _omniOptions = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_omniConnection)
            .Options;
        using var omniCtx = new OmniCardDbContext(_omniOptions);
        omniCtx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _omniConnection.Dispose();
    }

    private CardService CreateService() => new(
        new StubHashService(),
        [],
        new MockOmniDbContextFactory(_omniOptions),
        new StubOcrService(),
        new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
        NullLogger<CardService>.Instance,
        new DataPathService(Path.GetTempPath()),
        new NullScanDiagnosticService(),
        new NullAuditService());

    /// <summary>Seeds a Product (deduped by caller as needed) + one InventoryLot copy of it.</summary>
    private static InventoryLot SeedLot(OmniCardDbContext ctx, Product product, string? condition = "NM",
        int? containerId = null, decimal? unitCost = null, bool isMissing = false, FlagReason? flagReason = null)
    {
        if (product.Id == 0)
        {
            ctx.Products.Add(product);
            ctx.SaveChanges();
        }

        var lot = new InventoryLot
        {
            ProductId = product.Id,
            Condition = condition,
            LocationId = containerId,
            UnitCost = unitCost,
            IsMissing = isMissing,
            FlagReason = flagReason,
        };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot;
    }

    private static Product MakeProduct(string gameCardId, string name, CardGame game = CardGame.Mtg,
        bool foil = false, string setCode = "lea", string setName = "Alpha", string rarity = "common",
        string? color = null, string? cardType = null) => new()
    {
        Game = game,
        Category = ProductCategory.Single,
        GameCardId = gameCardId,
        Name = name,
        Foil = foil,
        SetCode = setCode,
        SetName = setName,
        Rarity = rarity,
        Color = color,
        CardType = cardType,
    };

    // --- Stacking ---

    [Fact]
    public void SearchCollection_Stacked_GroupsByGameCardIdFoilCondition()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        var bolt = MakeProduct("bolt-1", "Lightning Bolt");
        SeedLot(ctx, bolt, condition: "NM");
        SeedLot(ctx, bolt, condition: "NM");
        SeedLot(ctx, bolt, condition: "NM");
        // Different condition -> separate stack.
        SeedLot(ctx, bolt, condition: "LP");
        // Different card entirely -> separate stack.
        SeedLot(ctx, MakeProduct("counterspell-1", "Counterspell"), condition: "NM");

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("", CardGame.Mtg, null, null, null, stacked: true, results);

        Assert.Equal(3, results.Count); // Bolt/NM, Bolt/LP, Counterspell/NM

        var boltNm = Assert.Single(results, c => c.Name == "Lightning Bolt" && c.Condition == "NM");
        Assert.Equal(3, boltNm.Quantity);
        Assert.NotNull(boltNm.StackedIds);
        Assert.Equal(3, boltNm.StackedIds!.Count);

        var boltLp = Assert.Single(results, c => c.Name == "Lightning Bolt" && c.Condition == "LP");
        Assert.Equal(1, boltLp.Quantity);

        var counterspell = Assert.Single(results, c => c.Name == "Counterspell");
        Assert.Equal(1, counterspell.Quantity);
    }

    [Fact]
    public void SearchCollection_Stacked_DifferentFoilness_NotStackedTogether()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        SeedLot(ctx, MakeProduct("bolt-1", "Lightning Bolt", foil: false), condition: "NM");
        SeedLot(ctx, MakeProduct("bolt-1", "Lightning Bolt", foil: true), condition: "NM");

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("", CardGame.Mtg, null, null, null, stacked: true, results);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, c => c.IsFoil && c.Quantity == 1);
        Assert.Contains(results, c => !c.IsFoil && c.Quantity == 1);
    }

    [Fact]
    public void SearchCollection_Unstacked_ReturnsOneRowPerLot()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        var bolt = MakeProduct("bolt-1", "Lightning Bolt");
        SeedLot(ctx, bolt);
        SeedLot(ctx, bolt);
        SeedLot(ctx, bolt);

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("", CardGame.Mtg, null, null, null, stacked: false, results);

        Assert.Equal(3, results.Count);
        Assert.All(results, c => Assert.Equal(1, c.Quantity));
        // Ids are distinct lot ids.
        Assert.Equal(3, results.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void GetSearchCount_Stacked_CountsGroupsNotLots()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        var bolt = MakeProduct("bolt-1", "Lightning Bolt");
        SeedLot(ctx, bolt);
        SeedLot(ctx, bolt);
        SeedLot(ctx, MakeProduct("counterspell-1", "Counterspell"));

        var service = CreateService();

        Assert.Equal(2, service.GetSearchCount("", CardGame.Mtg, null, null, stacked: true));
        Assert.Equal(3, service.GetSearchCount("", CardGame.Mtg, null, null, stacked: false));
    }

    // --- Sort ---

    [Fact]
    public void SearchCollection_NoPreset_SortsByNameAscending()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        SeedLot(ctx, MakeProduct("z-1", "Zap"));
        SeedLot(ctx, MakeProduct("a-1", "Ambush Viper"));
        SeedLot(ctx, MakeProduct("m-1", "Mulldrifter"));

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("", CardGame.Mtg, null, null, null, results);

        Assert.Equal(["Ambush Viper", "Mulldrifter", "Zap"], results.Select(c => c.Name).ToList());
    }

    [Fact]
    public void SearchCollection_SortPreset_DescendingByName()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        SeedLot(ctx, MakeProduct("z-1", "Zap"));
        SeedLot(ctx, MakeProduct("a-1", "Ambush Viper"));

        var sort = new SortPreset
        {
            Name = "Name Desc",
            Game = CardGame.Mtg,
            SortLevels = [new SortLevel { Field = "Name", Direction = SortDirection.Descending }],
        };

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("", CardGame.Mtg, null, sort, null, results);

        Assert.Equal(["Zap", "Ambush Viper"], results.Select(c => c.Name).ToList());
    }

    [Fact]
    public void SearchCollection_SortPreset_CustomColorOrder()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        SeedLot(ctx, MakeProduct("g-1", "Green Card", color: "G"));
        SeedLot(ctx, MakeProduct("w-1", "White Card", color: "W"));
        SeedLot(ctx, MakeProduct("u-1", "Blue Card", color: "U"));

        var sort = new SortPreset
        {
            Name = "Color Sort",
            Game = CardGame.Mtg,
            SortLevels = [new SortLevel { Field = "Color", Direction = SortDirection.Ascending, CustomOrder = ["W", "U", "B", "R", "G"] }],
        };

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("", CardGame.Mtg, null, sort, null, results);

        Assert.Equal(["White Card", "Blue Card", "Green Card"], results.Select(c => c.Name).ToList());
    }

    // --- Filter ---

    [Fact]
    public void SearchCollection_ScryfallQuery_FiltersByName()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        SeedLot(ctx, MakeProduct("bolt-1", "Lightning Bolt"));
        SeedLot(ctx, MakeProduct("cs-1", "Counterspell"));

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("lightning", CardGame.Mtg, null, null, null, results);

        Assert.Single(results);
        Assert.Equal("Lightning Bolt", results[0].Name);
    }

    [Fact]
    public void SearchCollection_FilterPreset_AppliesScryfallQuery()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        SeedLot(ctx, MakeProduct("bolt-1", "Lightning Bolt", cardType: "Instant"));
        SeedLot(ctx, MakeProduct("elves-1", "Llanowar Elves", cardType: "Creature"));

        var filter = new FilterPreset { Name = "Instants", Game = CardGame.Mtg, Query = "t:instant" };

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("", CardGame.Mtg, null, null, filter, results);

        Assert.Single(results);
        Assert.Equal("Lightning Bolt", results[0].Name);
    }

    [Fact]
    public void SearchCollection_GameFilter_ExcludesOtherGames()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        SeedLot(ctx, MakeProduct("bolt-1", "Lightning Bolt", game: CardGame.Mtg));
        SeedLot(ctx, MakeProduct("op-1", "Zoro", game: CardGame.OnePiece));

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("", CardGame.OnePiece, null, null, null, results);

        Assert.Single(results);
        Assert.Equal("Zoro", results[0].Name);
    }

    [Fact]
    public void SearchCollection_ContainerFilter_OnlyMatchingLocation()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        var container = new StorageContainer { Name = "Binder A", ContainerType = ContainerType.Binder };
        ctx.StorageContainers.Add(container);
        ctx.SaveChanges();

        SeedLot(ctx, MakeProduct("bolt-1", "Lightning Bolt"), containerId: container.Id);
        SeedLot(ctx, MakeProduct("cs-1", "Counterspell"), containerId: null);

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("", CardGame.Mtg, container.Id, null, null, results);

        Assert.Single(results);
        Assert.Equal("Lightning Bolt", results[0].Name);
    }

    [Fact]
    public void SearchCollection_IsMissingDb_FiltersToFlaggedLots()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        SeedLot(ctx, MakeProduct("bolt-1", "Lightning Bolt"));
        SeedLot(ctx, MakeProduct("unk-1", "Unknown Card"), isMissing: true, flagReason: FlagReason.MissingFromDatabase);

        var filter = new FilterPreset { Name = "Missing", Game = CardGame.Mtg, Query = "is:missingdb" };

        var results = new ObservableCollection<CollectionCard>();
        CreateService().SearchCollection("", CardGame.Mtg, null, null, filter, results);

        Assert.Single(results);
        Assert.Equal("Unknown Card", results[0].Name);
        Assert.True(results[0].IsMissing);
        Assert.Equal(FlagReason.MissingFromDatabase, results[0].FlagReason);
    }

    // --- Pagination ---

    [Fact]
    public void SearchCollection_SkipTake_PaginatesConsistently()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        for (var i = 0; i < 5; i++)
            SeedLot(ctx, MakeProduct($"c-{i}", $"Card {i:D2}"));

        var service = CreateService();

        var page1 = new ObservableCollection<CollectionCard>();
        service.SearchCollection("", CardGame.Mtg, null, null, null, stacked: false, skip: 0, take: 2, page1);
        var page2 = new ObservableCollection<CollectionCard>();
        service.SearchCollection("", CardGame.Mtg, null, null, null, stacked: false, skip: 2, take: 2, page2);
        var page3 = new ObservableCollection<CollectionCard>();
        service.SearchCollection("", CardGame.Mtg, null, null, null, stacked: false, skip: 4, take: 2, page3);

        Assert.Equal(["Card 00", "Card 01"], page1.Select(c => c.Name).ToList());
        Assert.Equal(["Card 02", "Card 03"], page2.Select(c => c.Name).ToList());
        Assert.Equal(["Card 04"], page3.Select(c => c.Name).ToList());
    }

    // --- GetMatchingContainerIds / GetDistinctFieldValues / GetCollectionCards ---

    [Fact]
    public void GetMatchingContainerIds_ReturnsOnlyContainersWithMatch()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        var binder = new StorageContainer { Name = "Binder", ContainerType = ContainerType.Binder };
        var box = new StorageContainer { Name = "Box", ContainerType = ContainerType.Box };
        ctx.StorageContainers.AddRange(binder, box);
        ctx.SaveChanges();

        SeedLot(ctx, MakeProduct("bolt-1", "Lightning Bolt"), containerId: binder.Id);
        SeedLot(ctx, MakeProduct("cs-1", "Counterspell"), containerId: box.Id);

        var result = CreateService().GetMatchingContainerIds("Lightning Bolt", CardGame.Mtg);

        Assert.Single(result);
        Assert.Contains(binder.Id, result);
    }

    [Fact]
    public void GetDistinctFieldValues_ReturnsSortedDistinctValuesForGame()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        SeedLot(ctx, MakeProduct("bolt-1", "Lightning Bolt", color: "R"));
        SeedLot(ctx, MakeProduct("bolt-2", "Lightning Bolt Copy", color: "R"));
        SeedLot(ctx, MakeProduct("cs-1", "Counterspell", color: "U"));
        SeedLot(ctx, MakeProduct("op-1", "Zoro", game: CardGame.OnePiece, color: "Green"));

        var colors = CreateService().GetDistinctFieldValues("Color", CardGame.Mtg);

        Assert.Equal(["R", "U"], colors);
    }

    [Fact]
    public void GetCollectionCards_ReturnsCardsMatchingLotIds()
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        var bolt = SeedLot(ctx, MakeProduct("bolt-1", "Lightning Bolt"));
        var cs = SeedLot(ctx, MakeProduct("cs-1", "Counterspell"));
        SeedLot(ctx, MakeProduct("elves-1", "Llanowar Elves")); // not requested

        var result = CreateService().GetCollectionCards([bolt.Id, cs.Id]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Id == bolt.Id && c.Name == "Lightning Bolt");
        Assert.Contains(result, c => c.Id == cs.Id && c.Name == "Counterspell");
    }

    // --- Stubs ---

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

    private class MockOmniDbContextFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
