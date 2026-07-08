using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Helpers;
using OmniCard.Imaging;
using OmniCard.Models;
using OmniCard.CardMatching;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

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
            new OptcgCard { CardSetId = "OP01-001", CardName = "Luffy", SetId = "OP01", SetName = "Romance Dawn", Rarity = "SR", CardColor = "Red", CardType = "Leader", CardCost = "5", CardPower = "5000", CardText = "Rush", CardImageUri = "https://example.com/op01-001.jpg" },
            new OptcgCard { CardSetId = "OP01-002", CardName = "Zoro", SetId = "OP01", SetName = "Romance Dawn", Rarity = "SR", CardColor = "Green", CardType = "Character", CardCost = "3", CardPower = "4000", CardText = "Slash", CardImageUri = "https://example.com/op01-002.jpg" },
            new OptcgCard { CardSetId = "OP01-003", CardName = "Nami", SetId = "OP01", SetName = "Romance Dawn", Rarity = "R", CardColor = "Blue", CardType = "Character", CardCost = "2", CardPower = "3000", CardText = "Draw 1", CardImageUri = "https://example.com/op01-003.jpg" },
            new OptcgCard { CardSetId = "OP02-001", CardName = "Ace", SetId = "OP02", SetName = "Paramount War", Rarity = "SR", CardColor = "Red", CardType = "Character", CardCost = "4", CardPower = "5000", CardText = "Fire", CardImageUri = "https://example.com/op02-001.jpg" },
            new OptcgCard { CardSetId = "OP02-002", CardName = "Marco", SetId = "OP02", SetName = "Paramount War", Rarity = "R", CardColor = "Blue", CardType = "Character", CardCost = "3", CardPower = "4000", CardText = "Heal", CardImageUri = "https://example.com/op02-002.jpg" }
        );
        ctx.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private OptcgService CreateService()
    {
        return new OptcgService(
            new StubHttpClientFactory(),
            _factory,
            new PerceptualHashService(Microsoft.Extensions.Logging.Abstractions.NullLogger<PerceptualHashService>.Instance),
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
