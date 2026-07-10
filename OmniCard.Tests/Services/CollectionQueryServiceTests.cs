using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class CollectionQueryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<CollectionDbContext> _factory;

    public CollectionQueryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection).Options;
        _factory = new TestDbContextFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CollectionQueryService CreateService(
        List<StorageContainer> containers,
        Dictionary<string, decimal>? prices = null)
    {
        var mockContainerService = new Mock<IStorageContainerService>();
        mockContainerService.Setup(c => c.GetAll()).Returns(containers);

        var mockGameService = new Mock<ICardGameService>();
        mockGameService
            .Setup(g => g.GetCurrentPrices(It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
            .Returns((IEnumerable<string> ids, bool _) =>
            {
                if (prices is null) return new Dictionary<string, decimal>();
                var result = new Dictionary<string, decimal>();
                foreach (var id in ids.Distinct())
                    if (prices.TryGetValue(id, out var p))
                        result[id] = p;
                return result;
            });

        var mockCardService = new Mock<ICardService>();
        mockCardService
            .Setup(c => c.GetGameService(It.IsAny<CardGame>()))
            .Returns(mockGameService.Object);

        return new CollectionQueryService(_factory, mockContainerService.Object, mockCardService.Object);
    }

    private StorageContainer SeedContainer(string name, ContainerType type = ContainerType.Binder, int? coverCardId = null)
    {
        using var ctx = _factory.CreateDbContext();
        var container = new StorageContainer
        {
            Name = name,
            ContainerType = type,
            CoverCardId = coverCardId,
        };
        ctx.StorageContainers.Add(container);
        ctx.SaveChanges();
        return container;
    }

    private CollectionCard SeedCard(int containerId, string gameCardId, string name,
        CardGame game = CardGame.Mtg, decimal? purchasePrice = null,
        bool isFoil = false, string? imageUri = null)
    {
        using var ctx = _factory.CreateDbContext();
        var card = new CollectionCard
        {
            Game = game,
            GameCardId = gameCardId,
            Name = name,
            SetName = "TestSet",
            SetCode = "TST",
            Number = "1",
            Rarity = "common",
            ContainerId = containerId,
            PurchasePrice = purchasePrice,
            IsFoil = isFoil,
            ImageUri = imageUri,
        };
        ctx.Cards.Add(card);
        ctx.SaveChanges();
        return card;
    }

    [Fact]
    public async Task GetLocationOverviews_NoContainers_ReturnsEmpty()
    {
        var svc = CreateService([]);
        var result = await svc.GetLocationOverviewsAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetLocationOverviews_ContainersWithNoCards_ReturnsZeroCounts()
    {
        var container = SeedContainer("Empty Box", ContainerType.Box);
        var svc = CreateService([container]);

        var result = await svc.GetLocationOverviewsAsync();

        var summary = Assert.Single(result);
        Assert.Equal(0, summary.CardCount);
        Assert.Equal(0m, summary.TotalPurchaseCost);
        Assert.Equal(0m, summary.TotalMarketValue);
    }

    [Fact]
    public async Task GetLocationOverviews_CorrectCardCountAndPurchaseTotal()
    {
        var container = SeedContainer("Binder");
        SeedCard(container.Id, "c1", "Card A", purchasePrice: 5.00m);
        SeedCard(container.Id, "c2", "Card B", purchasePrice: 3.00m);

        var svc = CreateService([container]);
        var result = await svc.GetLocationOverviewsAsync();

        var summary = Assert.Single(result);
        Assert.Equal(2, summary.CardCount);
        Assert.Equal(8.00m, summary.TotalPurchaseCost);
    }

    [Fact]
    public async Task GetLocationOverviews_GameFilter_OnlyCountsMatchingGame()
    {
        var container = SeedContainer("Mixed");
        SeedCard(container.Id, "mtg1", "MTG Card", game: CardGame.Mtg, purchasePrice: 10m);
        SeedCard(container.Id, "op1", "OP Card", game: CardGame.OnePiece, purchasePrice: 5m);

        var svc = CreateService([container]);
        var result = await svc.GetLocationOverviewsAsync(CardGame.Mtg);

        var summary = Assert.Single(result);
        Assert.Equal(1, summary.CardCount);
        Assert.Equal(10m, summary.TotalPurchaseCost);
    }

    [Fact]
    public async Task GetLocationOverviews_MarketValue_UsesGameServicePrices()
    {
        var container = SeedContainer("Priced");
        SeedCard(container.Id, "c1", "Expensive", purchasePrice: 1m);
        SeedCard(container.Id, "c2", "Cheap", purchasePrice: 1m);

        var prices = new Dictionary<string, decimal>
        {
            ["c1"] = 10.00m,
            ["c2"] = 2.00m,
        };
        var svc = CreateService([container], prices);
        var result = await svc.GetLocationOverviewsAsync();

        var summary = Assert.Single(result);
        Assert.Equal(12.00m, summary.TotalMarketValue);
    }

    [Fact]
    public async Task GetLocationOverviews_PriceDelta_CalculatesCorrectly()
    {
        var container = SeedContainer("Delta");
        SeedCard(container.Id, "c1", "Card", purchasePrice: 10m);

        var prices = new Dictionary<string, decimal> { ["c1"] = 15.00m };
        var svc = CreateService([container], prices);
        var result = await svc.GetLocationOverviewsAsync();

        var summary = Assert.Single(result);
        Assert.Equal(5.00m, summary.PriceDelta);        // 15 - 10
        Assert.Equal(50.0, summary.PriceDeltaPercent);   // (5/10)*100
    }

    [Fact]
    public async Task GetLocationOverviews_PriceDelta_ZeroPurchase_ZeroPercent()
    {
        var container = SeedContainer("Free");
        SeedCard(container.Id, "c1", "Card", purchasePrice: null);

        var prices = new Dictionary<string, decimal> { ["c1"] = 5.00m };
        var svc = CreateService([container], prices);
        var result = await svc.GetLocationOverviewsAsync();

        var summary = Assert.Single(result);
        Assert.Equal(0.0, summary.PriceDeltaPercent); // no division by zero
    }

    [Fact]
    public async Task GetLocationOverviews_CoverImage_FromExplicitCoverCardId()
    {
        var container = SeedContainer("WithCover");
        var card = SeedCard(container.Id, "c1", "Cover Card", imageUri: "https://img/cover.jpg");

        // Update container's CoverCardId in DB
        using (var ctx = _factory.CreateDbContext())
        {
            var c = ctx.StorageContainers.Find(container.Id)!;
            c.CoverCardId = card.Id;
            ctx.SaveChanges();
        }
        // Pass updated container to mock
        container.CoverCardId = card.Id;

        var svc = CreateService([container]);
        var result = await svc.GetLocationOverviewsAsync();

        var summary = Assert.Single(result);
        Assert.Equal("https://img/cover.jpg", summary.CoverImageUri);
    }

    [Fact]
    public async Task GetLocationOverviews_CoverImage_FallbackToFirstCard()
    {
        var container = SeedContainer("NoCover");
        SeedCard(container.Id, "c1", "First Card", imageUri: "https://img/first.jpg");

        var svc = CreateService([container]);
        var result = await svc.GetLocationOverviewsAsync();

        var summary = Assert.Single(result);
        Assert.NotNull(summary.CoverImageUri);
    }

    private class TestDbContextFactory(DbContextOptions<CollectionDbContext> options)
        : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }
}
