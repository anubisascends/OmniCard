using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Collection;

namespace OmniCard.Tests.Services;

public class AnalyticsServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;

    public AnalyticsServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new OmniCardDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private IAnalyticsService CreateService(params ICardGameService[] gameServices) =>
        new AnalyticsService(new MockFactory(_options), gameServices);

    private static Product SeedProduct(OmniCardDbContext ctx, CardGame game, ProductCategory category,
        string name, string? gameCardId = null, bool foil = false, decimal marketPrice = 0m, decimal? lastMarketPrice = null)
    {
        var product = new Product
        {
            Game = game,
            Category = category,
            Name = name,
            GameCardId = gameCardId,
            Foil = foil,
            MarketPrice = marketPrice,
            LastMarketPrice = lastMarketPrice,
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        return product;
    }

    private static InventoryLot SeedLot(OmniCardDbContext ctx, int productId, int quantity, decimal? unitCost, int? locationId)
    {
        var lot = new InventoryLot
        {
            ProductId = productId,
            Quantity = quantity,
            UnitCost = unitCost,
            LocationId = locationId,
        };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot;
    }

    private static void SeedMovement(OmniCardDbContext ctx, int productId, int? lotId, MovementType type, int quantity, decimal? unitValue)
    {
        ctx.Movements.Add(new InventoryMovement
        {
            ProductId = productId,
            LotId = lotId,
            Type = type,
            Quantity = quantity,
            UnitValue = unitValue,
        });
        ctx.SaveChanges();
    }

    // --- Holdings ---

    [Fact]
    public void GetHoldings_ComputesTotals_AndBreakdowns_ForSinglesAndSealed()
    {
        using var ctx = new OmniCardDbContext(_options);
        var container = new StorageContainer { Name = "Binder A", ContainerType = ContainerType.Binder };
        ctx.StorageContainers.Add(container);
        ctx.SaveChanges();

        var bolt = SeedProduct(ctx, CardGame.Mtg, ProductCategory.Single, "Lightning Bolt", "bolt-1", foil: false);
        SeedLot(ctx, bolt.Id, 2, 1.50m, container.Id);

        var shock = SeedProduct(ctx, CardGame.Mtg, ProductCategory.Single, "Shock", "shock-1", foil: true);
        SeedLot(ctx, shock.Id, 1, 3.00m, null); // Unassigned location

        var opCard = SeedProduct(ctx, CardGame.OnePiece, ProductCategory.Single, "Zoro", "op-1", foil: false);
        SeedLot(ctx, opCard.Id, 3, 2.00m, container.Id);

        // Sealed product: market comes from the persisted Product.LastMarketPrice (Task 1,
        // Phase 3 — eBay-derived). Product.MarketPrice ([NotMapped]) is irrelevant for sealed.
        var box = SeedProduct(ctx, CardGame.Mtg, ProductCategory.Box, "Bloomburrow Booster Box",
            marketPrice: 999m, lastMarketPrice: 30.00m);
        SeedLot(ctx, box.Id, 2, 20.00m, container.Id);

        var mtgService = new FakeCardGameService(CardGame.Mtg, new Dictionary<(string, bool), decimal>
        {
            [("bolt-1", false)] = 5.00m,
            [("shock-1", true)] = 8.00m,
        });
        var opService = new FakeCardGameService(CardGame.OnePiece, new Dictionary<(string, bool), decimal>
        {
            [("op-1", false)] = 1.00m,
        });

        var service = CreateService(mtgService, opService);
        var holdings = service.GetHoldings();

        Assert.Equal(8, holdings.TotalUnits);
        Assert.Equal(52.00m, holdings.TotalCost);
        // 2*5 + 1*8 + 3*1 (singles, live) + 2*30 (sealed box, LastMarketPrice) = 81
        Assert.Equal(81.00m, holdings.TotalMarket);

        var byGame = holdings.ByGame.ToDictionary(l => l.Key);
        Assert.Equal(5, byGame["Mtg"].Units);
        Assert.Equal(46.00m, byGame["Mtg"].Cost);
        Assert.Equal(78.00m, byGame["Mtg"].Market); // 10 (bolt) + 8 (shock) + 60 (box, 2*30)
        Assert.Equal(3, byGame["OnePiece"].Units);
        Assert.Equal(6.00m, byGame["OnePiece"].Cost);
        Assert.Equal(3.00m, byGame["OnePiece"].Market);

        var byCategory = holdings.ByCategory.ToDictionary(l => l.Key);
        Assert.Equal(6, byCategory["Single"].Units);
        Assert.Equal(12.00m, byCategory["Single"].Cost);
        Assert.Equal(21.00m, byCategory["Single"].Market);
        Assert.Equal(2, byCategory["Box"].Units);
        Assert.Equal(40.00m, byCategory["Box"].Cost);
        Assert.Equal(60.00m, byCategory["Box"].Market); // LastMarketPrice-derived, not 0

        var byLocation = holdings.ByLocation.ToDictionary(l => l.Key);
        Assert.Equal(7, byLocation["Binder A"].Units);
        Assert.Equal(49.00m, byLocation["Binder A"].Cost);
        Assert.Equal(73.00m, byLocation["Binder A"].Market); // 10 (bolt) + 3 (op) + 60 (box)
        Assert.Equal(1, byLocation["Unassigned"].Units);
        Assert.Equal(3.00m, byLocation["Unassigned"].Cost);
        Assert.Equal(8.00m, byLocation["Unassigned"].Market);

        // Breakdowns reconcile to totals.
        Assert.Equal(holdings.TotalUnits, holdings.ByGame.Sum(l => l.Units));
        Assert.Equal(holdings.TotalCost, holdings.ByGame.Sum(l => l.Cost));
        Assert.Equal(holdings.TotalMarket, holdings.ByGame.Sum(l => l.Market));
        Assert.Equal(holdings.TotalUnits, holdings.ByCategory.Sum(l => l.Units));
        Assert.Equal(holdings.TotalUnits, holdings.ByLocation.Sum(l => l.Units));
    }

    [Fact]
    public void GetHoldings_NoLots_ReturnsZeroedTotals()
    {
        var service = CreateService();
        var holdings = service.GetHoldings();

        Assert.Equal(0, holdings.TotalUnits);
        Assert.Equal(0m, holdings.TotalCost);
        Assert.Equal(0m, holdings.TotalMarket);
        Assert.Empty(holdings.ByGame);
        Assert.Empty(holdings.ByCategory);
        Assert.Empty(holdings.ByLocation);
    }

    // --- Realized ---

    [Fact]
    public void GetRealized_PairsSellWithAcquire_ByLot_IncludingLotsSinceDeleted()
    {
        using var ctx = new OmniCardDbContext(_options);

        var bolt = SeedProduct(ctx, CardGame.Mtg, ProductCategory.Single, "Lightning Bolt", "bolt-1");
        var boltLot = SeedLot(ctx, bolt.Id, 2, 1.50m, null);
        SeedMovement(ctx, bolt.Id, boltLot.Id, MovementType.Acquire, 2, 1.50m); // cost 3
        SeedMovement(ctx, bolt.Id, boltLot.Id, MovementType.Sell, 2, 5.00m);   // proceeds 10
        // Simulate the lot having since been deleted; its movement history persists.
        ctx.Lots.Remove(ctx.Lots.Single(l => l.Id == boltLot.Id));
        ctx.SaveChanges();

        var op = SeedProduct(ctx, CardGame.OnePiece, ProductCategory.Single, "Zoro", "op-1");
        var opLot = SeedLot(ctx, op.Id, 1, 2.00m, null);
        SeedMovement(ctx, op.Id, opLot.Id, MovementType.Acquire, 1, 2.00m); // cost 2
        SeedMovement(ctx, op.Id, opLot.Id, MovementType.Sell, 1, 1.00m);    // proceeds 1 (loss)

        // Unsold lot: only an Acquire movement, no Sell -> excluded entirely.
        var unsold = SeedProduct(ctx, CardGame.Mtg, ProductCategory.Single, "Counterspell", "cs-1");
        var unsoldLot = SeedLot(ctx, unsold.Id, 5, 1.00m, null);
        SeedMovement(ctx, unsold.Id, unsoldLot.Id, MovementType.Acquire, 5, 1.00m);

        var service = CreateService();
        var realized = service.GetRealized();

        Assert.Equal(3, realized.TotalSold); // 2 (bolt) + 1 (op)
        Assert.Equal(11.00m, realized.TotalProceeds); // 10 + 1
        Assert.Equal(5.00m, realized.TotalCost); // 3 + 2
        Assert.Equal(6.00m, realized.TotalProceeds - realized.TotalCost); // profit

        var byGame = realized.ByGame.ToDictionary(l => l.Key);
        Assert.Equal(2, byGame["Mtg"].Count);
        Assert.Equal(10.00m, byGame["Mtg"].Proceeds);
        Assert.Equal(3.00m, byGame["Mtg"].Cost);
        Assert.Equal(1, byGame["OnePiece"].Count);
        Assert.Equal(1.00m, byGame["OnePiece"].Proceeds);
        Assert.Equal(2.00m, byGame["OnePiece"].Cost);

        // Unsold lot contributes nothing to totals or breakdowns.
        Assert.Equal(3, byGame.Values.Sum(l => l.Count));
    }

    [Fact]
    public void GetRealized_NoSales_ReturnsEmptySummary()
    {
        using var ctx = new OmniCardDbContext(_options);
        var product = SeedProduct(ctx, CardGame.Mtg, ProductCategory.Single, "Lightning Bolt", "bolt-1");
        var lot = SeedLot(ctx, product.Id, 3, 1.00m, null);
        SeedMovement(ctx, product.Id, lot.Id, MovementType.Acquire, 3, 1.00m);

        var service = CreateService();
        var realized = service.GetRealized();

        Assert.Equal(0, realized.TotalSold);
        Assert.Equal(0m, realized.TotalProceeds);
        Assert.Equal(0m, realized.TotalCost);
        Assert.Empty(realized.ByGame);
    }

    // --- Test doubles ---

    private class FakeCardGameService(CardGame game, Dictionary<(string GameCardId, bool IsFoil), decimal> prices) : ICardGameService
    {
        public CardGame Game => game;
        public MatchDiagnostics? LastMatchDiagnostics => null;
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) => null;
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => [];
        public List<CardMatch> GetPrintings(string cardName) => [];
        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => prices.GetValueOrDefault((gameCardId, isFoil));
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil)
        {
            var result = new Dictionary<string, decimal>();
            foreach (var id in gameCardIds)
                if (prices.TryGetValue((id, isFoil), out var p))
                    result[id] = p;
            return result;
        }
        public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
        public IReadOnlyList<SetInfo> GetAvailableSets() => [];
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
        public object? FindCardById(string gameCardId) => null;
    }

    private class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
