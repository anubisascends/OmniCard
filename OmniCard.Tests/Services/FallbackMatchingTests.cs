using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class FallbackMatchingTests : IDisposable
{
    private readonly SqliteConnection _collectionConnection;
    private readonly DbContextOptions<CollectionDbContext> _collectionOptions;

    public FallbackMatchingTests()
    {
        _collectionConnection = new SqliteConnection("Data Source=:memory:");
        _collectionConnection.Open();
        _collectionOptions = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_collectionConnection)
            .Options;
        using var ctx = new CollectionDbContext(_collectionOptions);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _collectionConnection.Dispose();

    [Fact]
    public void FindBestMatch_ReturnsMatchFromOtherGame_WhenPrimaryFails()
    {
        var expectedMatch = new CardMatch
        {
            Name = "Lightning Bolt",
            SetCode = "lea",
            SetName = "Alpha",
            CollectorNumber = "1",
            Rarity = "common",
            GameSpecificId = Guid.NewGuid().ToString(),
            Source = new Card { Id = Guid.NewGuid(), Name = "Lightning Bolt" },
        };

        var noMatchService = new StubGameService(CardGame.OnePiece, match: null);
        var matchService = new StubGameService(CardGame.Mtg, match: expectedMatch);

        var service = CreateCardService([noMatchService, matchService]);

        // Selected game is OnePiece (first in enum order), which has no match
        service.SelectedGame = CardGame.OnePiece;

        var (match, game) = service.FindBestMatch(0xDEADBEEF);

        Assert.NotNull(match);
        Assert.Equal("Lightning Bolt", match.Name);
        Assert.Equal(CardGame.Mtg, game);
    }

    [Fact]
    public void FindBestMatch_ReturnsPrimaryGame_WhenPrimaryMatches()
    {
        var expectedMatch = new CardMatch
        {
            Name = "Zoro",
            SetCode = "OP01",
            SetName = "Romance Dawn",
            CollectorNumber = "OP01-001",
            Rarity = "SR",
            GameSpecificId = "OP01-001",
            Source = new OptcgCard { CardSetId = "OP01-001", CardName = "Zoro" },
        };

        var primaryService = new StubGameService(CardGame.OnePiece, match: expectedMatch);
        var otherService = new StubGameService(CardGame.Mtg, match: null);

        var service = CreateCardService([primaryService, otherService]);
        service.SelectedGame = CardGame.OnePiece;

        var (match, game) = service.FindBestMatch(0xDEADBEEF);

        Assert.NotNull(match);
        Assert.Equal("Zoro", match.Name);
        Assert.Equal(CardGame.OnePiece, game);
    }

    [Fact]
    public void FindBestMatch_ReturnsNull_WhenNoGameMatches()
    {
        var noMatch1 = new StubGameService(CardGame.Mtg, match: null);
        var noMatch2 = new StubGameService(CardGame.OnePiece, match: null);

        var service = CreateCardService([noMatch1, noMatch2]);
        service.SelectedGame = CardGame.Mtg;

        var (match, game) = service.FindBestMatch(0xDEADBEEF);

        Assert.Null(match);
    }

    [Fact]
    public void FindBestMatch_WithSetFilter_SkipsCrossGameFallback()
    {
        var otherMatch = new CardMatch
        {
            Name = "Lightning Bolt",
            SetCode = "lea",
            SetName = "Alpha",
            CollectorNumber = "1",
            Rarity = "common",
            GameSpecificId = Guid.NewGuid().ToString(),
            Source = new Card { Id = Guid.NewGuid(), Name = "Lightning Bolt" },
        };

        // Primary game returns null, other game has a match
        var noMatchService = new StubGameService(CardGame.OnePiece, match: null);
        var matchService = new StubGameService(CardGame.Mtg, match: otherMatch);

        var service = CreateCardService([noMatchService, matchService]);
        service.SelectedGame = CardGame.OnePiece;

        // With set filter, should NOT fall back to MTG
        var (match, game) = service.FindBestMatch(0xDEADBEEF, setFilter: new HashSet<string> { "OP01" });

        Assert.Null(match);
        Assert.Equal(CardGame.OnePiece, game);
    }

    [Fact]
    public void ReprocessScans_MatchesOnlyUnmatchedCards()
    {
        var mtgMatch = new CardMatch
        {
            Name = "Bolt",
            SetCode = "lea",
            SetName = "Alpha",
            CollectorNumber = "1",
            Rarity = "common",
            GameSpecificId = Guid.NewGuid().ToString(),
            Source = new Card { Id = Guid.NewGuid(), Name = "Bolt" },
        };

        var existingMatch = new CardMatch
        {
            Name = "Zoro",
            SetCode = "OP01",
            SetName = "Romance Dawn",
            CollectorNumber = "OP01-001",
            Rarity = "SR",
            GameSpecificId = "OP01-001",
            Source = new OptcgCard { CardSetId = "OP01-001", CardName = "Zoro" },
        };

        // MTG service matches hash 0xAABB, OnePiece matches nothing
        var mtgService = new StubGameService(CardGame.Mtg, match: mtgMatch);
        var opService = new StubGameService(CardGame.OnePiece, match: null);

        var service = CreateCardService([mtgService, opService]);
        service.SelectedGame = CardGame.OnePiece;

        // Add an unmatched card and an already-matched card
        var unmatched = CreateScannedCard(CardGame.OnePiece, hash: 0xAABB, match: null);
        var matched = CreateScannedCard(CardGame.OnePiece, hash: 0xCCDD, match: existingMatch);
        service.ScannedCards.Add(unmatched);
        service.ScannedCards.Add(matched);

        service.ReprocessScans();

        // Unmatched card should now have a match and game switched to MTG
        Assert.NotNull(unmatched.Match);
        Assert.Equal("Bolt", unmatched.Match.Name);
        Assert.Equal(CardGame.Mtg, unmatched.Game);

        // Already-matched card should be unchanged
        Assert.Equal("Zoro", matched.Match!.Name);
        Assert.Equal(CardGame.OnePiece, matched.Game);
    }

    private CardSevice CreateCardService(ICardGameService[] gameServices)
    {
        var factory = new MockCollectionDbContextFactory(_collectionOptions);
        return new CardSevice(
            new StubHashService(),
            gameServices,
            factory,
            new StubOcrService(),
            new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
            NullLogger<CardSevice>.Instance,
            new DataPathService(Path.GetTempPath()));
    }

    private static ScannedCard CreateScannedCard(CardGame game, ulong hash, CardMatch? match)
    {
        return new ScannedCard
        {
            TempImagePath = System.IO.Path.GetTempFileName(),
            Hash = hash,
            Game = game,
            Match = match,
        };
    }

    // --- Helpers ---

    private class StubGameService(CardGame game, CardMatch? match) : ICardGameService
    {
        public CardGame Game => game;
        public MatchDiagnostics? LastMatchDiagnostics => null;
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 10) => match;
        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => null;
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => [];
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => [];
        public List<CardMatch> GetPrintings(string cardName) => [];
        public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
        public IReadOnlyList<SetInfo> GetAvailableSets() => [];
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
        public object? FindCardById(string gameCardId) => null;
    }

    private class StubHashService : IPerceptualHashService
    {
        public ulong ComputeHash(System.IO.Stream imageStream, Action<OmniCard.Models.HashStageResult>? onStage = null) => 0;
        public ulong[] ComputeArtHash(System.IO.Stream imageStream, (double X, double Y, double W, double H)[] cropRegions, Action<OmniCard.Models.HashStageResult>? onStage = null) => new ulong[cropRegions.Length];
    }

    private class StubOcrService : IOcrMatchingService
    {
        public Dictionary<string, ulong> SymbolHashes { get; set; } = [];
        public Task<OcrMatchResult> AnalyzeCardAsync(byte[] imageData) => Task.FromResult(new OcrMatchResult());
        public (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] imageData) => ([], 0);
    }

    private class MockCollectionDbContextFactory(DbContextOptions<CollectionDbContext> options) : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }
}
