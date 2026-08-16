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
using OmniCard.Tests.Fakes;

namespace OmniCard.Tests.Services;

public class CardServiceCollectionTests : IDisposable
{
    private readonly SqliteConnection _omniConnection;
    private readonly DbContextOptions<OmniCardDbContext> _omniOptions;

    public CardServiceCollectionTests()
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
        CreateOmniFactory(),
        new StubOcrService(),
        new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
        NullLogger<CardService>.Instance,
        new DataPathService(Path.GetTempPath()),
        new NullScanDiagnosticService(),
        new NullAuditService(),
        new StubScannerSettingsService());

    private CardService CreateServiceWithGame(ICardGameService game) => new(
        new StubHashService(),
        [game],
        CreateOmniFactory(),
        new StubOcrService(),
        new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
        NullLogger<CardService>.Instance,
        new DataPathService(Path.GetTempPath()),
        new NullScanDiagnosticService(),
        new NullAuditService(),
        new StubScannerSettingsService());

    [Fact]
    public void ImportCollectionCards_ResolvesColorAndType_FromCatalog_WhenMissing()
    {
        var cardId = Guid.NewGuid();
        var game = new ConfigurableGameService
        {
            OnFindCardById = id => id == cardId.ToString()
                ? new Card { Id = cardId, Colors = ["R"], TypeLine = "Creature — Goblin" }
                : null,
        };
        var svc = CreateServiceWithGame(game);

        svc.ImportCollectionCards(
            [new CollectionCard { Game = CardGame.Mtg, GameCardId = cardId.ToString(), Name = "Goblin", Condition = "NM" }],
            skipDuplicates: false);

        using var ctx = new OmniCardDbContext(_omniOptions);
        var product = ctx.Products.Single();
        Assert.Equal("R", product.Color);
        Assert.Equal("Creature", product.CardType);
    }

    [Fact]
    public void ImportCollectionCards_BackfillsColor_OnExistingColorlessProduct()
    {
        var cardId = Guid.NewGuid().ToString();

        // First import with no catalog resolver → product created with null colour (the legacy bug).
        CreateServiceWithGame(new ConfigurableGameService()).ImportCollectionCards(
            [new CollectionCard { Game = CardGame.Mtg, GameCardId = cardId, Name = "X", Condition = "NM" }],
            skipDuplicates: false);
        using (var ctx = new OmniCardDbContext(_omniOptions))
            Assert.True(string.IsNullOrEmpty(ctx.Products.Single().Color));

        // Re-import the same printing once the catalog resolves it → existing product is backfilled.
        var resolver = new ConfigurableGameService
        {
            OnFindCardById = _ => new Card { Colors = ["G"], TypeLine = "Creature" },
        };
        CreateServiceWithGame(resolver).ImportCollectionCards(
            [new CollectionCard { Game = CardGame.Mtg, GameCardId = cardId, Name = "X", Condition = "NM" }],
            skipDuplicates: false);

        using (var ctx = new OmniCardDbContext(_omniOptions))
        {
            var product = ctx.Products.Single(); // same identity, not duplicated
            Assert.Equal("G", product.Color);
            Assert.Equal(2, ctx.Lots.Count());   // both imports recorded a lot
        }
    }

    [Fact]
    public void ChunkedByIdLookup_ReturnsAllRows_AcrossChunkBoundaries()
    {
        // The crux of the "too many SQL variables" fix: the id-keyed lookup must return every row
        // even when the id count exceeds the chunk size (i.e. it must not stop after the first
        // chunk or drop the remainder). Uses a tiny chunk size so 5 ids span 3 chunks (2,2,1).
        var rows = Enumerable.Range(1, 5).Select(i => new { Id = i, Val = $"v{i}" }).ToList();
        var ids = rows.Select(r => r.Id).ToList();

        var map = CardService.ChunkedByIdLookup(
            ids,
            chunk => rows.Where(r => chunk.Contains(r.Id)),
            r => r.Id,
            chunkSize: 2);

        Assert.Equal(5, map.Count);
        Assert.All(ids, id => Assert.Equal($"v{id}", map[id].Val));
    }

    [Fact]
    public void GetUnplacedBinderCards_ExcludesPlacedCards_AndAppliesFilterPreset()
    {
        int containerId;
        using (var ctx = new OmniCardDbContext(_omniOptions))
        {
            var container = new StorageContainer { Name = "Binder A", ContainerType = ContainerType.Binder };
            ctx.StorageContainers.Add(container);
            ctx.SaveChanges();
            containerId = container.Id;

            var sorcery = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Wrath of God", CardType = "Sorcery" };
            var instant = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Counterspell", CardType = "Instant" };
            var placedSorcery = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Day of Judgment", CardType = "Sorcery" };
            ctx.Products.AddRange(sorcery, instant, placedSorcery);
            ctx.SaveChanges();

            ctx.Lots.AddRange(
                new InventoryLot { ProductId = sorcery.Id, LocationId = containerId, Page = null },
                new InventoryLot { ProductId = instant.Id, LocationId = containerId, Page = null },
                new InventoryLot { ProductId = placedSorcery.Id, LocationId = containerId, Page = 1, Slot = 0 });
            ctx.SaveChanges();
        }

        var service = CreateService();

        var all = service.GetUnplacedBinderCards(containerId, filterPreset: null);
        Assert.Equal(2, all.Count);
        Assert.DoesNotContain(all, c => c.Name == "Day of Judgment");

        var sorceriesOnly = service.GetUnplacedBinderCards(containerId,
            new FilterPreset { Name = "Sorceries", Game = CardGame.Mtg, Query = "type:sorcery" });
        Assert.Single(sorceriesOnly);
        Assert.Equal("Wrath of God", sorceriesOnly[0].Name);
    }

    [Fact]
    public void SearchCollection_AllGames_CrossesChunkBoundary_AttachesEveryListing()
    {
        // Integration guard: "All Games" (gameFilter == null) loads the whole collection unpaginated
        // (take == int.MaxValue), then AttachEbayListings looks up eBay rows by lot id. That lookup
        // is now chunked (see ChunkedByIdLookup) to avoid SQLite's "too many SQL variables" cap.
        // Seed enough cards to cross the 500-id chunk boundary and verify EF translates the
        // per-chunk `int[].Contains(...)` and that no listing is dropped across chunks.
        const int count = 1100;
        using (var ctx = new OmniCardDbContext(_omniOptions))
        {
            var products = Enumerable.Range(0, count).Select(i => new Product
            {
                Game = i % 2 == 0 ? CardGame.Mtg : CardGame.Pokemon,
                Category = ProductCategory.Single,
                GameCardId = $"c{i}",
                Name = $"Card {i}",
                SetName = "Set",
            }).ToList();
            ctx.Products.AddRange(products);
            ctx.SaveChanges();

            var lots = products.Select(p => new InventoryLot { ProductId = p.Id }).ToList();
            ctx.Lots.AddRange(lots);
            ctx.SaveChanges();

            var listings = lots.Select(l => new EbayListing { LotId = l.Id }).ToList();
            ctx.EbayListings.AddRange(listings);
            ctx.SaveChanges();
        }

        var service = CreateService();
        var results = new ObservableCollection<CollectionCard>();

        var ex = Record.Exception(() => service.SearchCollection("", null, results));

        Assert.Null(ex);
        Assert.Equal(count, results.Count);
        Assert.All(results, c => Assert.NotNull(c.EbayListing));
    }

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
        public Task<(string? CollectorNumber, double Confidence)> DetectRiftboundCollectorNumberAsync(byte[] imageData) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? CollectorNumber, double Confidence)> DetectCollectorNumberAsync(byte[] imageData, OcrCollectorSpec spec) => Task.FromResult<(string?, double)>((null, 0));
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

    private class StubScannerSettingsService : IScannerSettingsService
    {
        public ScanWorkflowMode WorkflowMode { get; private set; } = ScanWorkflowMode.Store;
        public void SetWorkflowMode(ScanWorkflowMode mode) => WorkflowMode = mode;
    }
}
