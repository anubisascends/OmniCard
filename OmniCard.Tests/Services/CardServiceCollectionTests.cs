using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class CardServiceCollectionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CollectionDbContext> _options;

    public CardServiceCollectionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new CollectionDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private IDbContextFactory<CollectionDbContext> CreateFactory() => new MockCollectionDbContextFactory(_options);

    [Fact]
    public void SearchCollection_NoFilter_ReturnsAllGames()
    {
        using (var ctx = new CollectionDbContext(_options))
        {
            ctx.Cards.Add(new CollectionCard { Game = CardGame.Mtg, GameCardId = "id1", Name = "MTG Card" });
            ctx.Cards.Add(new CollectionCard { Game = CardGame.OnePiece, GameCardId = "id2", Name = "OP Card" });
            ctx.SaveChanges();
        }

        var service = new CardSevice(
            new StubHashService(),
            [],
            CreateFactory(),
            new StubOcrService(),
            new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
            NullLogger<CardSevice>.Instance,
            new DataPathService(Path.GetTempPath()));

        var results = new ObservableCollection<CollectionCard>();
        service.SearchCollection("", null, results);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void SearchCollection_WithGameFilter_ReturnsOnlyThatGame()
    {
        using (var ctx = new CollectionDbContext(_options))
        {
            ctx.Cards.Add(new CollectionCard { Game = CardGame.Mtg, GameCardId = "id1", Name = "MTG Card" });
            ctx.Cards.Add(new CollectionCard { Game = CardGame.OnePiece, GameCardId = "id2", Name = "OP Card" });
            ctx.SaveChanges();
        }

        var service = new CardSevice(
            new StubHashService(),
            [],
            CreateFactory(),
            new StubOcrService(),
            new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
            NullLogger<CardSevice>.Instance,
            new DataPathService(Path.GetTempPath()));

        var results = new ObservableCollection<CollectionCard>();
        service.SearchCollection("", CardGame.OnePiece, results);

        Assert.Single(results);
        Assert.Equal("OP Card", results[0].Name);
    }

    [Fact]
    public void SearchCollection_WithQuery_FiltersbyNameOrSet()
    {
        using (var ctx = new CollectionDbContext(_options))
        {
            ctx.Cards.Add(new CollectionCard { Game = CardGame.Mtg, GameCardId = "id1", Name = "Lightning Bolt", SetName = "Alpha" });
            ctx.Cards.Add(new CollectionCard { Game = CardGame.Mtg, GameCardId = "id2", Name = "Counterspell", SetName = "Alpha" });
            ctx.SaveChanges();
        }

        var service = new CardSevice(
            new StubHashService(),
            [],
            CreateFactory(),
            new StubOcrService(),
            new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
            NullLogger<CardSevice>.Instance,
            new DataPathService(Path.GetTempPath()));

        var results = new ObservableCollection<CollectionCard>();
        service.SearchCollection("Lightning", null, results);

        Assert.Single(results);
        Assert.Equal("Lightning Bolt", results[0].Name);
    }

    [Fact]
    public void CommitScans_WritesToCollectionDb()
    {
        var service = new CardSevice(
            new StubHashService(),
            [],
            CreateFactory(),
            new StubOcrService(),
            new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
            NullLogger<CardSevice>.Instance,
            new DataPathService(Path.GetTempPath()));

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

        using var ctx = new CollectionDbContext(_options);
        var cards = ctx.Cards.AsNoTracking().OrderBy(c => c.Name).ToList();
        Assert.Equal(2, cards.Count);

        Assert.Equal(CardGame.Mtg, cards[0].Game);
        Assert.Equal("Bolt", cards[0].Name);

        Assert.Equal(CardGame.OnePiece, cards[1].Game);
        Assert.Equal("Zoro", cards[1].Name);
        Assert.Equal("OP01-001", cards[1].GameCardId);
    }

    [Fact]
    public void CommitScans_PopulatesColorAndCardType()
    {
        var service = new CardSevice(
            new StubHashService(),
            [],
            CreateFactory(),
            new StubOcrService(),
            new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
            NullLogger<CardSevice>.Instance,
            new DataPathService(Path.GetTempPath()));

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

        using var ctx = new CollectionDbContext(_options);
        var cards = ctx.Cards.AsNoTracking().OrderBy(c => c.Name).ToList();

        Assert.Equal("R", cards[0].Color);
        Assert.Equal("Instant", cards[0].CardType);

        Assert.Equal("Green", cards[1].Color);
        Assert.Equal("Character", cards[1].CardType);
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

    // --- Helpers ---

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
