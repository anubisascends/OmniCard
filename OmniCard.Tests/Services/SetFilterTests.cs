using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Helpers;
using OmniCard.Imaging;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class ScryfallSetFilterTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<ScryfallDbContext> _factory;

    public ScryfallSetFilterTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ScryfallDbContext>()
            .UseSqlite(_connection)
            .Options;
        _factory = new TestScryfallDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();

        // Seed cards in two different sets with known hashes
        ctx.Cards.Add(new Card
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            OracleId = Guid.NewGuid(),
            Name = "Card In Set A",
            Lang = "en",
            Layout = "normal",
            TypeLine = "Creature",
            SetCode = "seta",
            SetName = "Set A",
            CollectorNumber = "001",
            Rarity = "common",
            ImageHash = 0x0000000000000000UL, // Closest to scan hash 0x01
        });
        ctx.Cards.Add(new Card
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            OracleId = Guid.NewGuid(),
            Name = "Card In Set B",
            Lang = "en",
            Layout = "normal",
            TypeLine = "Creature",
            SetCode = "setb",
            SetName = "Set B",
            CollectorNumber = "001",
            Rarity = "common",
            ImageHash = 0x0000000000000003UL, // Distance 2 from scan hash 0x01
        });
        ctx.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

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
    public void FindClosestMatch_NoFilter_ReturnsBestOverallMatch()
    {
        var svc = CreateService();
        // Hash 0x01: distance 1 from Set A card (0x00), distance 2 from Set B card (0x03)
        var match = svc.FindClosestMatch(0x0000000000000001UL);
        Assert.NotNull(match);
        Assert.Equal("Card In Set A", match.Name);
    }

    [Fact]
    public void FindClosestMatch_FilterToSetB_SkipsCloserSetACard()
    {
        var svc = CreateService();
        // Hash 0x01: distance 1 from Set A card, distance 2 from Set B card
        // With filter "setb", should skip Set A and return Set B
        var match = svc.FindClosestMatch(0x0000000000000001UL, setFilter: new HashSet<string> { "setb" });
        Assert.NotNull(match);
        Assert.Equal("Card In Set B", match.Name);
        Assert.Equal("setb", match.SetCode);
    }

    [Fact]
    public void FindClosestMatch_FilterToNonexistentSet_ReturnsNull()
    {
        var svc = CreateService();
        var match = svc.FindClosestMatch(0x0000000000000001UL, setFilter: new HashSet<string> { "nosuchset" });
        Assert.Null(match);
    }

    [Fact]
    public void FindClosestMatch_EmptyFilter_TreatedAsNoFilter()
    {
        var svc = CreateService();
        // Empty string should behave like null (no filter)
        var match = svc.FindClosestMatch(0x0000000000000001UL, setFilter: null);
        Assert.NotNull(match);
        Assert.Equal("Card In Set A", match.Name);
    }

    [Fact]
    public void GetAvailableSets_ReturnsDistinctSetsOrderedByName()
    {
        var svc = CreateService();
        var sets = svc.GetAvailableSets();
        Assert.Equal(2, sets.Count);
        Assert.Equal("Set A", sets[0].SetName);
        Assert.Equal("seta", sets[0].SetCode);
        Assert.Equal("Set B", sets[1].SetName);
        Assert.Equal("setb", sets[1].SetCode);
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

public class OptcgSetFilterTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;

    public OptcgSetFilterTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OptcgDbContext>()
            .UseSqlite(_connection)
            .Options;
        _factory = new TestOptcgDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();

        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = "OP01-001",
            CardName = "Card In OP01",
            SetId = "OP01",
            SetName = "Romance Dawn",
            Rarity = "C",
            CardColor = "Red",
            CardType = "Character",
            ImageHash = 0x0000000000000000UL,
        });
        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = "OP02-001",
            CardName = "Card In OP02",
            SetId = "OP02",
            SetName = "Paramount War",
            Rarity = "C",
            CardColor = "Blue",
            CardType = "Character",
            ImageHash = 0x0000000000000003UL,
        });
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
    public void FindClosestMatch_FilterToOP02_SkipsCloserOP01Card()
    {
        var svc = CreateService();
        var match = svc.FindClosestMatch(0x0000000000000001UL, setFilter: new HashSet<string> { "OP02" });
        Assert.NotNull(match);
        Assert.Equal("Card In OP02", match.Name);
        Assert.Equal("OP02", match.SetCode);
    }

    [Fact]
    public void FindClosestMatch_NoFilter_ReturnsBestOverall()
    {
        var svc = CreateService();
        var match = svc.FindClosestMatch(0x0000000000000001UL);
        Assert.NotNull(match);
        Assert.Equal("Card In OP01", match.Name);
    }

    [Fact]
    public void GetAvailableSets_ReturnsDistinctSets()
    {
        var svc = CreateService();
        var sets = svc.GetAvailableSets();
        Assert.Equal(2, sets.Count);
        // Ordered by SetName: "Paramount War" before "Romance Dawn"
        Assert.Equal("Paramount War", sets[0].SetName);
        Assert.Equal("OP02", sets[0].SetCode);
        Assert.Equal("Romance Dawn", sets[1].SetName);
        Assert.Equal("OP01", sets[1].SetCode);
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
