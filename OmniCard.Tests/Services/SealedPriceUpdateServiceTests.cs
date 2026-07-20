using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class SealedPriceUpdateServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;

    public SealedPriceUpdateServiceTests()
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

    private ISealedPriceUpdateService CreateService(IEbayCatalogService catalog, IEbayAuthService auth) =>
        new SealedPriceUpdateService(new MockFactory(_options), catalog, auth, NullLogger<SealedPriceUpdateService>.Instance);

    private static Product SeedProduct(OmniCardDbContext ctx, ProductCategory category, string name,
        string? setCode = null, string? upc = null)
    {
        var product = new Product
        {
            Game = CardGame.Mtg,
            Category = category,
            Name = name,
            SetCode = setCode,
            Upc = upc,
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        return product;
    }

    [Fact]
    public async Task RefreshSealedPricesAsync_Connected_UpdatesSealedProducts_SkipsZeroSampleCount_LeavesSinglesUntouched()
    {
        using var ctx = new OmniCardDbContext(_options);

        var box = SeedProduct(ctx, ProductCategory.Box, "Bloomburrow Booster Box", setCode: "blb");
        var pack = SeedProduct(ctx, ProductCategory.Pack, "Unknown Booster Pack", setCode: "xyz");
        var single = SeedProduct(ctx, ProductCategory.Single, "Lightning Bolt");

        var catalog = new FakeEbayCatalogService(new Dictionary<string, EbayMarketPrice?>
        {
            ["Bloomburrow Booster Box blb"] = new EbayMarketPrice { MedianPrice = 89.99m, LowPrice = 70m, HighPrice = 110m, SampleCount = 12 },
            ["Unknown Booster Pack xyz"] = new EbayMarketPrice { MedianPrice = 0m, LowPrice = 0m, HighPrice = 0m, SampleCount = 0 },
        });
        var auth = new FakeEbayAuthService("test-token");

        var service = CreateService(catalog, auth);
        var updated = await service.RefreshSealedPricesAsync();

        Assert.Equal(1, updated);

        using var verify = new OmniCardDbContext(_options);
        var boxAfter = verify.Products.Single(p => p.Id == box.Id);
        var packAfter = verify.Products.Single(p => p.Id == pack.Id);
        var singleAfter = verify.Products.Single(p => p.Id == single.Id);

        Assert.Equal(89.99m, boxAfter.LastMarketPrice);
        Assert.NotNull(boxAfter.PriceUpdatedAt);

        // SampleCount == 0 -> skipped, left null.
        Assert.Null(packAfter.LastMarketPrice);
        Assert.Null(packAfter.PriceUpdatedAt);

        // Singles are never touched by this service.
        Assert.Null(singleAfter.LastMarketPrice);
        Assert.Null(singleAfter.PriceUpdatedAt);
    }

    [Fact]
    public async Task RefreshSealedPricesAsync_PrefersUpc_WhenPresent()
    {
        using var ctx = new OmniCardDbContext(_options);
        var box = SeedProduct(ctx, ProductCategory.Box, "Bloomburrow Booster Box", setCode: "blb", upc: "012345678905");

        var catalog = new FakeEbayCatalogService(new Dictionary<string, EbayMarketPrice?>
        {
            ["012345678905"] = new EbayMarketPrice { MedianPrice = 75m, LowPrice = 60m, HighPrice = 90m, SampleCount = 5 },
        });
        var auth = new FakeEbayAuthService("test-token");

        var service = CreateService(catalog, auth);
        var updated = await service.RefreshSealedPricesAsync();

        Assert.Equal(1, updated);
        Assert.Equal(["012345678905"], catalog.Queries);

        using var verify = new OmniCardDbContext(_options);
        Assert.Equal(75m, verify.Products.Single(p => p.Id == box.Id).LastMarketPrice);
    }

    [Fact]
    public async Task RefreshSealedPricesAsync_Disconnected_IsNoOp_ReturnsZero()
    {
        using var ctx = new OmniCardDbContext(_options);
        var box = SeedProduct(ctx, ProductCategory.Box, "Bloomburrow Booster Box", setCode: "blb");

        var catalog = new FakeEbayCatalogService(new Dictionary<string, EbayMarketPrice?>
        {
            ["Bloomburrow Booster Box blb"] = new EbayMarketPrice { MedianPrice = 89.99m, SampleCount = 12 },
        });
        var auth = new FakeEbayAuthService(null); // not connected

        var service = CreateService(catalog, auth);

        // System.Progress<T> dispatches via the captured SynchronizationContext (or the thread
        // pool, asynchronously, if none) — not synchronously — so it can't be asserted right
        // after the awaited call returns. Use a directly-invoked IProgress<T> instead.
        var reportedMessages = new List<string>();
        var progress = new SynchronousProgress<PriceUpdateProgress>(p => reportedMessages.Add(p.Message));

        var updated = await service.RefreshSealedPricesAsync(progress);

        Assert.Equal(0, updated);
        Assert.Empty(catalog.Queries); // eBay never queried
        Assert.Contains(reportedMessages, m => m.Contains("Connect eBay"));

        using var verify = new OmniCardDbContext(_options);
        Assert.Null(verify.Products.Single(p => p.Id == box.Id).LastMarketPrice);
    }

    private class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}

// --- Test doubles ---

public class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}

public class FakeEbayCatalogService : IEbayCatalogService
{
    private readonly Dictionary<string, EbayMarketPrice?> _responses;
    public List<string> Queries { get; } = [];

    public FakeEbayCatalogService(Dictionary<string, EbayMarketPrice?> responses) => _responses = responses;

    public Task<List<EbayCatalogMatch>> SearchCatalogAsync(string cardName, string setName, string? collectorNumber) =>
        Task.FromResult(new List<EbayCatalogMatch>());

    public Task<EbayMarketPrice?> GetMarketPriceAsync(string searchQuery, string condition, bool isFoil)
    {
        Queries.Add(searchQuery);
        return Task.FromResult(_responses.GetValueOrDefault(searchQuery));
    }
}
