using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Interfaces;
using OmniCard.Audit;

namespace OmniCard.Tests.Services;

public class AuditServiceTests : IDisposable
{
    private readonly SqliteConnection _collectionConn;
    private readonly DbContextOptions<CollectionDbContext> _collectionOptions;
    private readonly SqliteConnection _scryfallConn;
    private readonly DbContextOptions<ScryfallDbContext> _scryfallOptions;

    public AuditServiceTests()
    {
        _collectionConn = new SqliteConnection("Data Source=:memory:");
        _collectionConn.Open();
        _collectionOptions = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_collectionConn)
            .Options;
        using var collCtx = new CollectionDbContext(_collectionOptions);
        collCtx.Database.EnsureCreated();

        _scryfallConn = new SqliteConnection("Data Source=:memory:");
        _scryfallConn.Open();
        _scryfallOptions = new DbContextOptionsBuilder<ScryfallDbContext>()
            .UseSqlite(_scryfallConn)
            .Options;
        using var scryfallCtx = new ScryfallDbContext(_scryfallOptions);
        scryfallCtx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _collectionConn.Dispose();
        _scryfallConn.Dispose();
    }

    private IDbContextFactory<CollectionDbContext> CollectionFactory() => new MockCollectionFactory(_collectionOptions);
    private IDbContextFactory<ScryfallDbContext> ScryfallFactory() => new MockScryfallFactory(_scryfallOptions);

    private AuditService CreateService() => new(
        CollectionFactory(),
        ScryfallFactory(),
        new StubContainerService(),
        NullLogger<AuditService>.Instance);

    [Fact]
    public void GenerateReport_MatchesOneToOneByGameCardId()
    {
        // Setup: location has 2x CardA and 1x CardB
        using (var ctx = new CollectionDbContext(_collectionOptions))
        {
            var container = new StorageContainer { Name = "Binder", ContainerType = ContainerType.Binder };
            ctx.StorageContainers.Add(container);
            ctx.SaveChanges();

            ctx.Cards.AddRange(
                new CollectionCard { Game = CardGame.Mtg, GameCardId = "card-a", Name = "Card A", SetCode = "SET", ContainerId = container.Id },
                new CollectionCard { Game = CardGame.Mtg, GameCardId = "card-a", Name = "Card A", SetCode = "SET", ContainerId = container.Id },
                new CollectionCard { Game = CardGame.Mtg, GameCardId = "card-b", Name = "Card B", SetCode = "SET", ContainerId = container.Id }
            );
            ctx.SaveChanges();
        }

        var service = CreateService();
        int containerId;
        using (var ctx = new CollectionDbContext(_collectionOptions))
            containerId = ctx.StorageContainers.First().Id;

        service.StartAudit(containerId);

        // Simulate: scanned 1x CardA and 1x unmatched
        var scans = new List<ScannedCard>
        {
            new() { TempImagePath = "t1.png", Hash = 0, Game = CardGame.Mtg,
                     Match = new CardMatch { GameSpecificId = "card-a", Name = "Card A", SetCode = "SET", Source = new object() } },
            new() { TempImagePath = "t2.png", Hash = 0, Game = CardGame.Mtg,
                     Match = null }, // unmatched scan
        };

        var report = service.GenerateReport(scans);

        Assert.Equal("Binder", report.LocationName);
        Assert.Equal(3, report.ExpectedCount);
        Assert.Equal(2, report.ActualCount);
        Assert.Single(report.Matched);                // 1x CardA matched
        Assert.Equal(2, report.Missing.Count);         // 1x CardA + 1x CardB missing
        Assert.Single(report.Extra);                   // 1x unmatched scan
    }

    [Fact]
    public void StartAudit_SetsActiveState()
    {
        using (var ctx = new CollectionDbContext(_collectionOptions))
        {
            var container = new StorageContainer { Name = "Box", ContainerType = ContainerType.Box };
            ctx.StorageContainers.Add(container);
            ctx.SaveChanges();
        }

        var service = CreateService();
        int containerId;
        using (var ctx = new CollectionDbContext(_collectionOptions))
            containerId = ctx.StorageContainers.First().Id;

        Assert.False(service.IsAuditActive);
        service.StartAudit(containerId);
        Assert.True(service.IsAuditActive);
        Assert.Equal(containerId, service.AuditLocationId);
        Assert.Equal("Box", service.AuditLocationName);

        service.EndAudit();
        Assert.False(service.IsAuditActive);
        Assert.Null(service.AuditLocationId);
    }

    [Fact]
    public void FindScopedMatch_MatchesOnlyLocationCards()
    {
        // Use hashes that are exactly 0 Hamming distance for an exact match,
        // and a completely different hash (all bits flipped = 64 distance) for no-match.
        // CardA hash: 0xAAAAAAAAAAAAAAAA (all alternating bits)
        // CardB hash: 0x5555555555555555 (complement of CardA — 64 bits different = distance 64)
        // Querying CardA's hash should match CardA (in location).
        // Querying CardB's hash: CardB is NOT in the location, so the scoped index has no entry
        // for it, and the only candidate (CardA) is 64 bits away — well above MaxDistance(14).
        const ulong cardAHash = 0xAAAA_AAAA_AAAA_AAAA;
        const ulong cardBHash = 0x5555_5555_5555_5555; // Hamming distance 64 from cardAHash

        Guid cardAId, cardBId;
        using (var ctx = new ScryfallDbContext(_scryfallOptions))
        {
            var cardA = CreateMinimalCard("Card A", "SET", "1", imageHash: cardAHash);
            var cardB = CreateMinimalCard("Card B", "SET", "2", imageHash: cardBHash);
            ctx.Cards.AddRange(cardA, cardB);
            ctx.SaveChanges();
            cardAId = cardA.Id;
            cardBId = cardB.Id;
        }

        using (var ctx = new CollectionDbContext(_collectionOptions))
        {
            var container = new StorageContainer { Name = "Binder", ContainerType = ContainerType.Binder };
            ctx.StorageContainers.Add(container);
            ctx.SaveChanges();
            // Only CardA is in the location
            ctx.Cards.Add(new CollectionCard
            {
                Game = CardGame.Mtg, GameCardId = cardAId.ToString(), Name = "Card A",
                SetCode = "SET", ContainerId = container.Id
            });
            ctx.SaveChanges();
        }

        var service = CreateService();
        int containerId;
        using (var ctx = new CollectionDbContext(_collectionOptions))
            containerId = ctx.StorageContainers.First().Id;

        service.StartAudit(containerId);

        // CardA's hash should match (exact match, distance 0)
        var matchA = service.FindScopedMatch(cardAHash, null);
        Assert.NotNull(matchA);
        Assert.Equal("Card A", matchA.Name);

        // CardB's hash: CardB is NOT in the location, so the scoped index only has CardA.
        // Distance from cardBHash to cardAHash is 64 (all bits flipped) — well above MaxDistance.
        // Should return null.
        var matchB = service.FindScopedMatch(cardBHash, null);
        Assert.Null(matchB);
    }

    // --- Helpers ---

    private static Card CreateMinimalCard(string name, string setCode, string collectorNumber, ulong? imageHash = null)
    {
        return new Card
        {
            Id = Guid.NewGuid(),
            OracleId = Guid.NewGuid(),
            Name = name,
            Lang = "en",
            ReleasedAt = "2024-01-01",
            Uri = "https://api.scryfall.com/cards/test",
            ScryfallUri = "https://scryfall.com/card/test",
            Layout = "normal",
            ImageStatus = "highres_scan",
            TypeLine = "Creature",
            ColorIdentity = [],
            Keywords = [],
            Games = ["paper"],
            Finishes = ["nonfoil"],
            SetId = Guid.NewGuid(),
            SetCode = setCode,
            SetName = "Test Set",
            SetType = "expansion",
            SetUri = "https://api.scryfall.com/sets/test",
            SetSearchUri = "https://api.scryfall.com/cards/search?q=test",
            ScryfallSetUri = "https://scryfall.com/sets/test",
            RulingsUri = "https://api.scryfall.com/cards/test/rulings",
            PrintsSearchUri = "https://api.scryfall.com/cards/search?q=test",
            CollectorNumber = collectorNumber,
            Rarity = "common",
            BorderColor = "black",
            Frame = "2015",
            Legalities = [],
            ImageHash = imageHash,
        };
    }

    // --- Stubs ---

    private class MockCollectionFactory(DbContextOptions<CollectionDbContext> options) : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }

    private class MockScryfallFactory(DbContextOptions<ScryfallDbContext> options) : IDbContextFactory<ScryfallDbContext>
    {
        public ScryfallDbContext CreateDbContext() => new(options);
    }

    private class StubContainerService : IStorageContainerService
    {
        // AuditService reads container name from CollectionDbContext directly — this stub is unused
        public List<StorageContainer> GetAll() => [];
        public StorageContainer GetBulk() => throw new NotImplementedException();
        public StorageContainer Create(string name, ContainerType type) => throw new NotImplementedException();
        public void Rename(int id, string newName) => throw new NotImplementedException();
        public void Delete(int id, bool moveCardsToBulk = true) => throw new NotImplementedException();
        public int GetCardCount(int containerId) => throw new NotImplementedException();
        public void SetCoverCard(int containerId, int? cardId) => throw new NotImplementedException();
        public List<CollectionCard> GetCardsInContainer(int containerId) => throw new NotImplementedException();
    }
}
