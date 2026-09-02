using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Models;
using OmniCard.CardMatching;
using OmniCard.Collection;
using OmniCard.Interfaces;

namespace OmniCard.Tests.Services;

public class CardServiceAllGamesSetCompletionTests : IDisposable
{
    private readonly SqliteConnection _omniConnection;
    private readonly DbContextOptions<OmniCardDbContext> _omniOptions;

    public CardServiceAllGamesSetCompletionTests()
    {
        _omniConnection = new SqliteConnection("Data Source=:memory:");
        _omniConnection.Open();
        _omniOptions = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_omniConnection)
            .Options;
        using var omniCtx = new OmniCardDbContext(_omniOptions);
        omniCtx.Database.EnsureCreated();
    }

    public void Dispose() => _omniConnection.Dispose();

    [Fact]
    public async Task CalculateSetCompletionAsync_NullGame_AggregatesAllGames()
    {
        var mtgService = new StubGameService(CardGame.Mtg, new SetCompletionSummary
        {
            SetCode = "seta",
            SetName = "Set A",
            Game = CardGame.Mtg,
            OwnedCount = 1,
            TotalCount = 3,
        });
        var onePieceService = new StubGameService(CardGame.OnePiece, new SetCompletionSummary
        {
            SetCode = "OP01",
            SetName = "Romance Dawn",
            Game = CardGame.OnePiece,
            OwnedCount = 2,
            TotalCount = 5,
        });

        var service = new CardService(
            new StubHashService(),
            [mtgService, onePieceService],
            new MockOmniDbContextFactory(_omniOptions),
            new StubOcrService(),
            new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
            NullLogger<CardService>.Instance,
            new DataPathService(Path.GetTempPath()),
            new NullScanDiagnosticService(),
            new NullAuditService(),
            new StubScannerSettingsService());

        var all = await service.CalculateSetCompletionAsync((CardGame?)null);

        Assert.Contains(all, s => s.Game == CardGame.Mtg && s.SetCode == "seta");
        Assert.Contains(all, s => s.Game == CardGame.OnePiece && s.SetCode == "OP01");
        Assert.Equal(1, mtgService.CallCount);
        Assert.Equal(1, onePieceService.CallCount);
    }

    [Fact]
    public void GetCurrentPrices_RoutesToGameService()
    {
        var mtgService = new StubGameService(CardGame.Mtg, new SetCompletionSummary { SetCode = "seta", Game = CardGame.Mtg });
        var service = new CardService(
            new StubHashService(),
            [mtgService],
            new MockOmniDbContextFactory(_omniOptions),
            new StubOcrService(),
            new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
            NullLogger<CardService>.Instance,
            new DataPathService(Path.GetTempPath()),
            new NullScanDiagnosticService(),
            new NullAuditService(),
            new StubScannerSettingsService());

        var prices = service.GetCurrentPrices(CardGame.Mtg, ["id1", "id2"], foil: true);

        Assert.Equal(2, prices.Count);
        Assert.Equal(1.23m, prices["id1"]);
        Assert.True(mtgService.LastGetCurrentPricesFoil);
    }

    private class StubGameService(CardGame game, SetCompletionSummary summary) : ICardGameService
    {
        public int CallCount { get; private set; }
        public bool LastGetCurrentPricesFoil { get; private set; }

        public CardGame Game => game;
        public MatchDiagnostics? LastMatchDiagnostics => null;
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) => null;
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => [];
        public List<CardMatch> GetPrintings(string cardName) => [];
        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => null;
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil)
        {
            LastGetCurrentPricesFoil = isFoil;
            var i = 0;
            var result = new Dictionary<string, decimal>();
            foreach (var id in gameCardIds)
                result[id] = 1.23m + i++;
            return result;
        }
        public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
        public IReadOnlyList<SetInfo> GetAvailableSets() => [];
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null)
        {
            CallCount++;
            return Task.FromResult(new List<SetCompletionSummary> { summary });
        }
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
        public object? FindCardById(string gameCardId) => null;
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
        public AuditReport GenerateFileAuditReport(int containerId, IEnumerable<CollectionCard> importedCards) => throw new NotImplementedException();
    }

    private class StubScannerSettingsService : IScannerSettingsService
    {
        public ScanWorkflowMode WorkflowMode { get; private set; } = ScanWorkflowMode.Store;
        public void SetWorkflowMode(ScanWorkflowMode mode) => WorkflowMode = mode;
    }
}

public class ScryfallSetCompletionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<ScryfallDbContext> _factory;

    public ScryfallSetCompletionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ScryfallDbContext>()
            .UseSqlite(_connection)
            .Options;
        _factory = new TestScryfallDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();

        // Seed: Set A has 3 cards, Set B has 2 cards
        ctx.Cards.AddRange(
            MakeCard("00000000-0000-0000-0000-000000000001", "Card A1", "seta", "Set A", "001", "common"),
            MakeCard("00000000-0000-0000-0000-000000000002", "Card A2", "seta", "Set A", "002", "uncommon"),
            MakeCard("00000000-0000-0000-0000-000000000003", "Card A3", "seta", "Set A", "003", "rare"),
            MakeCard("00000000-0000-0000-0000-000000000004", "Card B1", "setb", "Set B", "001", "common"),
            MakeCard("00000000-0000-0000-0000-000000000005", "Card B2", "setb", "Set B", "002", "mythic")
        );
        ctx.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private static Card MakeCard(string id, string name, string setCode, string setName, string cn, string rarity) => new()
    {
        Id = Guid.Parse(id),
        OracleId = Guid.NewGuid(),
        Name = name,
        Lang = "en",
        Layout = "normal",
        TypeLine = "Creature",
        SetCode = setCode,
        SetName = setName,
        CollectorNumber = cn,
        Rarity = rarity,
        ImageUris = new ImageUris { Normal = $"https://example.com/{setCode}/{cn}.jpg" },
    };

    private ScryfallService CreateService()
    {
        return new ScryfallService(
            new StubHttpClientFactory(),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            new SetSymbolCache(new StubHttpClientFactory(), new DataPathService(Path.GetTempPath()), NullLogger<SetSymbolCache>.Instance),
            Options.Create(new ScryfallSettings()),
            NullLogger<ScryfallService>.Instance,
            new DataPathService(Path.GetTempPath()));
    }

    [Fact]
    public async Task GetSetCompletionAsync_ReturnsCorrectCounts()
    {
        var svc = CreateService();
        // User owns 2 of 3 cards in Set A, 0 of 2 in Set B
        var owned = new List<CollectionCard>
        {
            new() { Game = CardGame.Mtg, SetCode = "seta", Number = "001" },
            new() { Game = CardGame.Mtg, SetCode = "seta", Number = "002" },
        };

        var results = await svc.GetSetCompletionAsync(owned);

        var setA = results.First(r => r.SetCode == "seta");
        Assert.Equal(2, setA.OwnedCount);
        Assert.Equal(3, setA.TotalCount);
        Assert.True(Math.Abs(setA.CompletionPercent - 66.67) < 0.1);

        var setB = results.First(r => r.SetCode == "setb");
        Assert.Equal(0, setB.OwnedCount);
        Assert.Equal(2, setB.TotalCount);
        Assert.Equal(0, setB.CompletionPercent);
    }

    [Fact]
    public async Task GetSetCompletionAsync_FullyCompleteSet_Returns100Percent()
    {
        var svc = CreateService();
        var owned = new List<CollectionCard>
        {
            new() { Game = CardGame.Mtg, SetCode = "setb", Number = "001" },
            new() { Game = CardGame.Mtg, SetCode = "setb", Number = "002" },
        };

        var results = await svc.GetSetCompletionAsync(owned);
        var setB = results.First(r => r.SetCode == "setb");
        Assert.Equal(2, setB.OwnedCount);
        Assert.Equal(2, setB.TotalCount);
        Assert.Equal(100, setB.CompletionPercent);
    }

    [Fact]
    public async Task GetSetCompletionAsync_EmptyCollection_AllZero()
    {
        var svc = CreateService();
        var results = await svc.GetSetCompletionAsync([]);

        Assert.All(results, r => Assert.Equal(0, r.OwnedCount));
        Assert.Equal(2, results.Count); // Both sets still appear
    }

    [Fact]
    public async Task GetSetCompletionAsync_SameSetCodeDifferentSetName_DoesNotThrow()
    {
        // Scryfall's bulk data occasionally has the same SetCode under two slightly different
        // SetName strings (e.g. localized/promo variants) — regression test for the duplicate-key
        // crash this used to cause.
        using var ctx = _factory.CreateDbContext();
        ctx.Cards.Add(MakeCard("00000000-0000-0000-0000-000000000006", "Card A4", "seta", "Set A (Alt)", "004", "common"));
        ctx.SaveChanges();

        var svc = CreateService();
        var results = await svc.GetSetCompletionAsync([]);

        var setA = Assert.Single(results, r => r.SetCode == "seta");
        Assert.Equal(4, setA.TotalCount); // merged despite the differing SetName
    }

    [Fact]
    public void GetMissingCards_ReturnsOnlyUnownedWithFullDetails()
    {
        var svc = CreateService();
        // Own card 001 in Set A, missing 002 and 003
        var missing = svc.GetMissingCards("seta", ["001"]);

        Assert.Equal(2, missing.Count);
        Assert.Contains(missing, m => m.Name == "Card A2" && m.CollectorNumber == "002");
        Assert.Contains(missing, m => m.Name == "Card A3" && m.CollectorNumber == "003");
        Assert.All(missing, m =>
        {
            Assert.Equal("seta", m.SetCode);
            Assert.NotNull(m.ImageUri);
            Assert.NotNull(m.TypeLine);
        });
    }

    [Fact]
    public void GetMissingCards_FullyComplete_ReturnsEmpty()
    {
        var svc = CreateService();
        var missing = svc.GetMissingCards("setb", ["001", "002"]);
        Assert.Empty(missing);
    }

    private class TestScryfallDbFactory(DbContextOptions<ScryfallDbContext> options) : IDbContextFactory<ScryfallDbContext>
    {
        public ScryfallDbContext CreateDbContext() => new(options);
    }

    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}

public class OptcgSetCompletionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;

    public OptcgSetCompletionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OptcgDbContext>()
            .UseSqlite(_connection)
            .Options;
        _factory = new TestOptcgDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();

        // Seed: OP01 has 3 cards, OP02 has 2 cards
        ctx.Cards.AddRange(
            new OptcgCard { CardSetId = "OP01-001", CardNumber = "OP01-001", CardName = "Luffy", SetId = "OP01", SetName = "Romance Dawn", Rarity = "SR", CardColor = "Red", CardType = "Leader", CardCost = "5", CardPower = "5000", CardText = "Rush", CardImageUri = "https://example.com/op01-001.jpg" },
            new OptcgCard { CardSetId = "OP01-002", CardNumber = "OP01-002", CardName = "Zoro", SetId = "OP01", SetName = "Romance Dawn", Rarity = "SR", CardColor = "Green", CardType = "Character", CardCost = "3", CardPower = "4000", CardText = "Slash", CardImageUri = "https://example.com/op01-002.jpg" },
            new OptcgCard { CardSetId = "OP01-003", CardNumber = "OP01-003", CardName = "Nami", SetId = "OP01", SetName = "Romance Dawn", Rarity = "R", CardColor = "Blue", CardType = "Character", CardCost = "2", CardPower = "3000", CardText = "Draw 1", CardImageUri = "https://example.com/op01-003.jpg" },
            new OptcgCard { CardSetId = "OP02-001", CardNumber = "OP02-001", CardName = "Ace", SetId = "OP02", SetName = "Paramount War", Rarity = "SR", CardColor = "Red", CardType = "Character", CardCost = "4", CardPower = "5000", CardText = "Fire", CardImageUri = "https://example.com/op02-001.jpg" },
            new OptcgCard { CardSetId = "OP02-002", CardNumber = "OP02-002", CardName = "Marco", SetId = "OP02", SetName = "Paramount War", Rarity = "R", CardColor = "Blue", CardType = "Character", CardCost = "3", CardPower = "4000", CardText = "Heal", CardImageUri = "https://example.com/op02-002.jpg" }
        );
        ctx.SaveChanges();
        ctx.MarkMigrationComplete();
    }

    public void Dispose() => _connection.Dispose();

    private OptcgService CreateService()
    {
        var dataPath = new Moq.Mock<OmniCard.Interfaces.IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(Path.GetTempPath());
        return new OptcgService(
            new StubHttpClientFactory(),
            _factory,
            new PerceptualHashService(Microsoft.Extensions.Logging.Abstractions.NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OptcgService>.Instance);
    }

    [Fact]
    public async Task GetSetCompletionAsync_ReturnsCorrectCounts()
    {
        var svc = CreateService();
        var owned = new List<CollectionCard>
        {
            new() { Game = CardGame.OnePiece, SetCode = "OP01", Number = "OP01-001" },
            new() { Game = CardGame.OnePiece, SetCode = "OP01", Number = "OP01-003" },
        };

        var results = await svc.GetSetCompletionAsync(owned);

        var op01 = results.First(r => r.SetCode == "OP01");
        Assert.Equal(2, op01.OwnedCount);
        Assert.Equal(3, op01.TotalCount);

        var op02 = results.First(r => r.SetCode == "OP02");
        Assert.Equal(0, op02.OwnedCount);
        Assert.Equal(2, op02.TotalCount);
    }

    [Fact]
    public void GetMissingCards_ReturnsUnownedWithMappedFields()
    {
        var svc = CreateService();
        var missing = svc.GetMissingCards("OP01", ["OP01-001"]);

        Assert.Equal(2, missing.Count);
        var zoro = missing.First(m => m.Name == "Zoro");
        Assert.Equal("OP01-002", zoro.CollectorNumber);
        Assert.Equal("OP01", zoro.SetCode);
        Assert.Equal("SR", zoro.Rarity);
        Assert.Equal("Character", zoro.TypeLine); // CardType → TypeLine
        Assert.Equal("Slash", zoro.OracleText);   // CardText → OracleText
        Assert.Equal("4000", zoro.Power);          // CardPower → Power
        Assert.Equal("Green", zoro.CardColor);
        Assert.Equal("3", zoro.CardCost);
        Assert.NotNull(zoro.ImageUri);
    }

    [Fact]
    public async Task GetSetCompletionAsync_EmptyCollection_AllZero()
    {
        var svc = CreateService();
        var results = await svc.GetSetCompletionAsync([]);

        Assert.All(results, r => Assert.Equal(0, r.OwnedCount));
        Assert.Equal(2, results.Count);
    }

    private class TestOptcgDbFactory(DbContextOptions<OptcgDbContext> options) : IDbContextFactory<OptcgDbContext>
    {
        public OptcgDbContext CreateDbContext() => new(options);
    }

    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
