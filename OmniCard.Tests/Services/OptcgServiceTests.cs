using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class OptcgServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;

    public OptcgServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OptcgDbContext>()
            .UseSqlite(_connection).Options;
        _factory = new TestOptcgDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();

        // Seed test cards with distinct hashes
        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = "OP01-001",
            CardName = "Monkey D. Luffy",
            SetId = "OP01",
            SetName = "Romance Dawn",
            Rarity = "SR",
            CardColor = "Red",
            CardType = "Leader",
            ImageHash = 0x0000000000000000UL,
            MarketPrice = 12.50m,
        });
        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = "OP01-002",
            CardName = "Roronoa Zoro",
            SetId = "OP01",
            SetName = "Romance Dawn",
            Rarity = "R",
            CardColor = "Green",
            CardType = "Character",
            ImageHash = 0x00000000000000FFUL, // Hamming distance 8 from 0x0
            MarketPrice = 5.00m,
        });
        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = "OP02-001",
            CardName = "Nami",
            SetId = "OP02",
            SetName = "Paramount War",
            Rarity = "C",
            CardColor = "Blue",
            CardType = "Character",
            ImageHash = 0xFFFFFFFFFFFFFFFFUL, // Hamming distance 64 from 0x0
            MarketPrice = 0.50m,
        });
        ctx.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private OptcgService CreateService()
    {
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(Path.GetTempPath());
        return new OptcgService(
            new StubHttpClientFactory(),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<OptcgService>.Instance);
    }

    // --- FindClosestMatch (pure pHash, no corrections) ---

    [Fact]
    public void FindClosestMatch_PHashDistance_ReturnsBestMatch()
    {
        var svc = CreateService();

        // Hash 0x01 is Hamming distance 1 from OP01-001 (0x0) and distance 7 from OP01-002 (0xFF)
        var match = svc.FindClosestMatch(0x0000000000000001UL);

        Assert.NotNull(match);
        Assert.Equal("Monkey D. Luffy", match.Name);
        Assert.Equal("OP01-001", match.GameSpecificId);
    }

    [Fact]
    public void FindClosestMatch_BeyondMaxDistance_ReturnsNull()
    {
        var svc = CreateService();

        // Hash that is very far from all seeded cards
        // OP01-001 = 0x0 (distance 32), OP01-002 = 0xFF (distance 24), OP02-001 = 0xFFFF... (distance 32)
        // Use maxDistance=2 so all cards are too far
        var match = svc.FindClosestMatch(0x00000000FFFFFFFFUL, maxDistance: 2);

        Assert.Null(match);
    }

    [Fact]
    public void FindClosestMatch_WithSetFilter_RespectsFilter()
    {
        var svc = CreateService();

        // Hash 0x01 is closest to OP01-001, but filter only allows OP02
        var setFilter = new HashSet<string> { "OP02" };
        var match = svc.FindClosestMatch(0x0000000000000001UL, setFilter: setFilter);

        // OP02-001 has hash 0xFFFF... which is distance 63 from 0x01 — beyond default maxDistance=14
        Assert.Null(match);
    }

    // --- SearchCards ---

    [Fact]
    public void SearchCards_Keyword_MatchesByName()
    {
        var svc = CreateService();
        var results = svc.SearchCards("Luffy");

        Assert.Single(results);
        Assert.Equal("Monkey D. Luffy", results[0].Name);
    }

    [Fact]
    public void SearchCards_SetQualifier_FiltersBySet()
    {
        var svc = CreateService();
        var results = svc.SearchCards("set:OP02");

        Assert.Single(results);
        Assert.Equal("Nami", results[0].Name);
        Assert.Equal("OP02", results[0].SetCode);
    }

    // --- Pricing ---

    [Fact]
    public void GetCurrentPrice_ReturnsStoredMarketPrice()
    {
        var svc = CreateService();
        var price = svc.GetCurrentPrice("OP01-001", isFoil: false);

        Assert.NotNull(price);
        Assert.Equal(12.50m, price.Value);
    }

    [Fact]
    public void GetCurrentPrices_ReturnsBatchPrices()
    {
        var svc = CreateService();
        var prices = svc.GetCurrentPrices(["OP01-001", "OP01-002", "NONEXISTENT"], isFoil: false);

        Assert.Equal(2, prices.Count);
        Assert.Equal(12.50m, prices["OP01-001"]);
        Assert.Equal(5.00m, prices["OP01-002"]);
        Assert.False(prices.ContainsKey("NONEXISTENT"));
    }

    private class TestOptcgDbFactory(DbContextOptions<OptcgDbContext> options)
        : IDbContextFactory<OptcgDbContext>
    {
        public OptcgDbContext CreateDbContext() => new(options);
    }

    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
