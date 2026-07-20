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

public class CardServiceCollectionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CollectionDbContext> _options;
    private readonly SqliteConnection _omniConnection;
    private readonly DbContextOptions<OmniCardDbContext> _omniOptions;

    public CardServiceCollectionTests()
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
    }

    public void Dispose()
    {
        _connection.Dispose();
        _omniConnection.Dispose();
    }

    private IDbContextFactory<CollectionDbContext> CreateFactory() => new MockCollectionDbContextFactory(_options);
    private IDbContextFactory<OmniCardDbContext> CreateOmniFactory() => new MockOmniDbContextFactory(_omniOptions);

    /// <summary>Seeds a Product+Lot pair (the unified-store equivalent of a single CollectionCard row).</summary>
    private static InventoryLot SeedCard(OmniCardDbContext ctx, CardGame game, string gameCardId, string name,
        string setName = "", int? containerId = null)
    {
        var product = new Product
        {
            Game = game,
            Category = ProductCategory.Single,
            GameCardId = gameCardId,
            Name = name,
            SetName = setName,
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();

        var lot = new InventoryLot { ProductId = product.Id, LocationId = containerId };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot;
    }

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
    public void SearchCollection_NoFilter_ReturnsAllGames()
    {
        using (var ctx = new OmniCardDbContext(_omniOptions))
        {
            SeedCard(ctx, CardGame.Mtg, "id1", "MTG Card");
            SeedCard(ctx, CardGame.OnePiece, "id2", "OP Card");
        }

        var service = CreateService();

        var results = new ObservableCollection<CollectionCard>();
        service.SearchCollection("", null, results);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void SearchCollection_WithGameFilter_ReturnsOnlyThatGame()
    {
        using (var ctx = new OmniCardDbContext(_omniOptions))
        {
            SeedCard(ctx, CardGame.Mtg, "id1", "MTG Card");
            SeedCard(ctx, CardGame.OnePiece, "id2", "OP Card");
        }

        var service = CreateService();

        var results = new ObservableCollection<CollectionCard>();
        service.SearchCollection("", CardGame.OnePiece, results);

        Assert.Single(results);
        Assert.Equal("OP Card", results[0].Name);
    }

    [Fact]
    public void SearchCollection_WithQuery_FiltersbyNameOrSet()
    {
        using (var ctx = new OmniCardDbContext(_omniOptions))
        {
            SeedCard(ctx, CardGame.Mtg, "id1", "Lightning Bolt", "Alpha");
            SeedCard(ctx, CardGame.Mtg, "id2", "Counterspell", "Alpha");
        }

        var service = CreateService();

        var results = new ObservableCollection<CollectionCard>();
        service.SearchCollection("Lightning", null, results);

        Assert.Single(results);
        Assert.Equal("Lightning Bolt", results[0].Name);
    }

    [Fact]
    public void CommitScans_WritesToCollectionDb()
    {
        var service = CreateService();

        var scans = new[]
        {
            CreateScan(CardGame.Mtg, new Card
            {
                Id = Guid.NewGuid(), Name = "Bolt", SetCode = "lea", SetName = "Alpha",
                CollectorNumber = "1", Rarity = "common", TypeLine = "Instant",
                ImageUris = new ImageUris { Normal = "https://img/bolt.jpg" }
            }),
            CreateScan(CardGame.OnePiece, new OptcgCard
            {
                CardSetId = "OP01-001", CardName = "Zoro", SetId = "OP01", SetName = "Romance Dawn",
                Rarity = "SR", CardColor = "Green", CardType = "Character",
                CardImageUri = "https://img/zoro.jpg"
            }),
        };

        service.CommitScans(scans);

        using var ctx = new OmniCardDbContext(_omniOptions);
        var lots = ctx.Lots.AsNoTracking().Include(l => l.Product).OrderBy(l => l.Product.Name).ToList();
        Assert.Equal(2, lots.Count);

        Assert.Equal(CardGame.Mtg, lots[0].Product.Game);
        Assert.Equal("Bolt", lots[0].Product.Name);

        Assert.Equal(CardGame.OnePiece, lots[1].Product.Game);
        Assert.Equal("Zoro", lots[1].Product.Name);
        Assert.Equal("OP01-001", lots[1].Product.GameCardId);
    }

    [Fact]
    public void CommitScans_PopulatesColorAndCardType()
    {
        var service = CreateService();

        var scans = new[]
        {
            CreateScan(CardGame.Mtg, new Card
            {
                Id = Guid.NewGuid(), Name = "Bolt", SetCode = "lea", SetName = "Alpha",
                CollectorNumber = "1", Rarity = "common", TypeLine = "Instant",
                Colors = ["R"],
                ImageUris = new ImageUris { Normal = "https://img/bolt.jpg" }
            }),
            CreateScan(CardGame.OnePiece, new OptcgCard
            {
                CardSetId = "OP01-001", CardName = "Zoro", SetId = "OP01", SetName = "Romance Dawn",
                Rarity = "SR", CardColor = "Green", CardType = "Character",
                CardImageUri = "https://img/zoro.jpg"
            }),
        };

        service.CommitScans(scans);

        using var ctx = new OmniCardDbContext(_omniOptions);
        var lots = ctx.Lots.AsNoTracking().Include(l => l.Product).OrderBy(l => l.Product.Name).ToList();

        Assert.Equal("R", lots[0].Product.Color);
        Assert.Equal("Instant", lots[0].Product.CardType);

        Assert.Equal("Green", lots[1].Product.Color);
        Assert.Equal("Character", lots[1].Product.CardType);
    }

    private static ScannedCard CreateScan(CardGame game, object sourceCard)
    {
        var match = sourceCard switch
        {
            Card c => new CardMatch
            {
                Name = c.Name, SetCode = c.SetCode, SetName = c.SetName,
                CollectorNumber = c.CollectorNumber, Rarity = c.Rarity,
                ImageUri = c.ImageUris?.Normal, GameSpecificId = c.Id.ToString(),
                Source = c,
            },
            OptcgCard c => new CardMatch
            {
                Name = c.CardName, SetCode = c.SetId, SetName = c.SetName,
                CollectorNumber = c.CardSetId, Rarity = c.Rarity,
                ImageUri = c.CardImageUri, GameSpecificId = c.CardSetId,
                Source = c,
            },
            _ => throw new ArgumentException("Unknown card type")
        };

        return new ScannedCard
        {
            TempImagePath = System.IO.Path.GetTempFileName(),
            Hash = 0,
            Game = game,
            Match = match,
        };
    }

    [Fact]
    public void GetMatchingContainerIds_ReturnsOnlyContainersWithMatchingCards()
    {
        int binderId, boxId;
        using (var ctx = new OmniCardDbContext(_omniOptions))
        {
            var binder = new StorageContainer { Name = "Binder", ContainerType = ContainerType.Binder };
            var box = new StorageContainer { Name = "Box", ContainerType = ContainerType.Box };
            ctx.StorageContainers.AddRange(binder, box);
            ctx.SaveChanges();
            binderId = binder.Id;
            boxId = box.Id;

            SeedCard(ctx, CardGame.Mtg, "id1", "Lightning Bolt", containerId: binderId);
            SeedCard(ctx, CardGame.Mtg, "id2", "Counterspell", containerId: boxId);
        }

        var service = CreateService();

        var result = service.GetMatchingContainerIds("Lightning Bolt", CardGame.Mtg);

        Assert.Single(result);
        // The binder has "Lightning Bolt", the box does not
        Assert.Contains(binderId, result);
    }

    [Fact]
    public void GetMatchingContainerIds_EmptyQuery_ReturnsAllContainers()
    {
        using (var ctx = new OmniCardDbContext(_omniOptions))
        {
            var binder = new StorageContainer { Name = "Binder2", ContainerType = ContainerType.Binder };
            var box = new StorageContainer { Name = "Box2", ContainerType = ContainerType.Box };
            ctx.StorageContainers.AddRange(binder, box);
            ctx.SaveChanges();

            SeedCard(ctx, CardGame.Mtg, "id10", "Card A", containerId: binder.Id);
            SeedCard(ctx, CardGame.Mtg, "id11", "Card B", containerId: box.Id);
        }

        var service = CreateService();

        var result = service.GetMatchingContainerIds("", CardGame.Mtg);

        Assert.True(result.Count >= 2);
    }

    // --- Helpers ---

    private class StubHashService : IPerceptualHashService
    {
        public ulong ComputeHash(System.IO.Stream imageStream, Action<OmniCard.Models.HashStageResult>? onStage = null) => 0;
        public ulong ComputeEdgeHash(System.IO.Stream imageStream, Action<OmniCard.Models.HashStageResult>? onStage = null) => 0;
        public ulong[] ComputeArtHash(System.IO.Stream imageStream, (double X, double Y, double W, double H)[] cropRegions, Action<OmniCard.Models.HashStageResult>? onStage = null) => new ulong[cropRegions.Length];
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

    private class MockCollectionDbContextFactory(DbContextOptions<CollectionDbContext> options) : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }

    private class MockOmniDbContextFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
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
