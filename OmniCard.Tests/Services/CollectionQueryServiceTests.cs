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
    private readonly IDbContextFactory<OmniCardDbContext> _factory;

    public CollectionQueryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OmniCardDbContext>()
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

        var listingService = new ListingService(_factory, new Mock<ISalesSettingsService>().Object);

        return new CollectionQueryService(_factory, mockContainerService.Object, mockCardService.Object, listingService);
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

    /// <summary>Seeds a Product+Lot pair (the unified-store equivalent of a single CollectionCard row).
    /// Returns the Lot, whose Id is the CollectionCard.Id replacement (used e.g. for CoverCardId).</summary>
    private InventoryLot SeedCard(int? containerId, string gameCardId, string name,
        CardGame game = CardGame.Mtg, decimal? purchasePrice = null,
        bool isFoil = false, string? imageUri = null, bool isTraded = false)
    {
        using var ctx = _factory.CreateDbContext();
        var product = new Product
        {
            Game = game,
            Category = ProductCategory.Single,
            GameCardId = gameCardId,
            Name = name,
            SetName = "TestSet",
            SetCode = "TST",
            CollectorNumber = "1",
            Rarity = "common",
            Foil = isFoil,
            ImageUri = imageUri,
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();

        var lot = new InventoryLot { ProductId = product.Id, LocationId = containerId, UnitCost = purchasePrice, IsTraded = isTraded };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot;
    }

    /// <summary>Seeds a sealed (non-single) Product+Lot pair, to verify GetTopValueCards excludes it.</summary>
    private InventoryLot SeedSealedProduct(int? containerId, string name, decimal? lastMarketPrice)
    {
        using var ctx = _factory.CreateDbContext();
        var product = new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Box,
            Name = name,
            LastMarketPrice = lastMarketPrice,
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();

        var lot = new InventoryLot { ProductId = product.Id, LocationId = containerId };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot;
    }

    private void SeedListing(int lotId, ListingStatus status = ListingStatus.Listed)
    {
        using var ctx = _factory.CreateDbContext();
        ctx.Listings.Add(new Listing { LotId = lotId, Status = status, ListedPrice = 0m });
        ctx.SaveChanges();
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

    [Fact]
    public void GetTopValueCards_RanksDescendingAndCapsAtTake()
    {
        var container = SeedContainer("Binder");
        SeedCard(container.Id, "c1", "Low", purchasePrice: 1m);
        SeedCard(container.Id, "c2", "High", purchasePrice: 1m);
        SeedCard(container.Id, "c3", "Mid", purchasePrice: 1m);

        var prices = new Dictionary<string, decimal> { ["c1"] = 5m, ["c2"] = 50m, ["c3"] = 20m };
        var svc = CreateService([container], prices);

        var result = svc.GetTopValueCards(2);

        Assert.Equal(2, result.Count);
        Assert.Equal("High", result[0].Name);
        Assert.Equal(20m, result[1].MarketPrice);
    }

    [Fact]
    public void GetTopValueCards_ExcludesActivelyListedLots()
    {
        var container = SeedContainer("Binder");
        var expensive = SeedCard(container.Id, "c1", "Expensive", purchasePrice: 1m);
        SeedCard(container.Id, "c2", "Cheap", purchasePrice: 1m);
        SeedListing(expensive.Id, ListingStatus.Listed);

        var prices = new Dictionary<string, decimal> { ["c1"] = 100m, ["c2"] = 1m };
        var svc = CreateService([container], prices);

        var result = svc.GetTopValueCards(100);

        var card = Assert.Single(result);
        Assert.Equal("Cheap", card.Name);
    }

    [Fact]
    public void GetTopValueCards_IncludesLotsWithNoOrInactiveListing()
    {
        var container = SeedContainer("Binder");
        var picked = SeedCard(container.Id, "c1", "Was Picked, Now Sold", purchasePrice: 1m);
        SeedListing(picked.Id, ListingStatus.Sold);

        var prices = new Dictionary<string, decimal> { ["c1"] = 10m };
        var svc = CreateService([container], prices);

        var result = svc.GetTopValueCards(100);

        Assert.Single(result);
    }

    [Fact]
    public void GetTopValueCards_ExcludesSealedProduct()
    {
        var container = SeedContainer("Binder");
        SeedSealedProduct(container.Id, "Booster Box", lastMarketPrice: 500m);
        SeedCard(container.Id, "c1", "Single", purchasePrice: 1m);

        var prices = new Dictionary<string, decimal> { ["c1"] = 5m };
        var svc = CreateService([container], prices);

        var result = svc.GetTopValueCards(100);

        var card = Assert.Single(result);
        Assert.Equal("Single", card.Name);
    }

    [Fact]
    public void GetTopValueCards_ExcludesTradedLots()
    {
        var container = SeedContainer("Binder");
        SeedCard(container.Id, "c1", "Traded Away", purchasePrice: 1m, isTraded: true);

        var prices = new Dictionary<string, decimal> { ["c1"] = 100m };
        var svc = CreateService([container], prices);

        var result = svc.GetTopValueCards(100);

        Assert.Empty(result);
    }

    [Fact]
    public void GetTopValueCards_PopulatesContainer_UnassignedWhenNoLocation()
    {
        var container = SeedContainer("Binder");
        SeedCard(container.Id, "c1", "Located", purchasePrice: 1m);
        SeedCard(null, "c2", "Unassigned", purchasePrice: 1m);

        var prices = new Dictionary<string, decimal> { ["c1"] = 5m, ["c2"] = 5m };
        var svc = CreateService([container], prices);

        var result = svc.GetTopValueCards(100);

        var located = Assert.Single(result, c => c.Name == "Located");
        Assert.Equal("Binder", located.Container?.Name);
        var unassigned = Assert.Single(result, c => c.Name == "Unassigned");
        Assert.Null(unassigned.Container);
    }

    [Fact]
    public void GetTopValueCards_SpansMultipleGames()
    {
        var container = SeedContainer("Binder");
        SeedCard(container.Id, "c1", "MTG Card", game: CardGame.Mtg, purchasePrice: 1m);
        SeedCard(container.Id, "c2", "OP Card", game: CardGame.OnePiece, purchasePrice: 1m);

        var prices = new Dictionary<string, decimal> { ["c1"] = 10m, ["c2"] = 20m };
        var svc = CreateService([container], prices);

        var result = svc.GetTopValueCards(100);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Game == CardGame.Mtg);
        Assert.Contains(result, c => c.Game == CardGame.OnePiece);
    }

    private class TestDbContextFactory(DbContextOptions<OmniCardDbContext> options)
        : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
