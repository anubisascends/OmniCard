using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class SetChecklistServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _options;

    public SetChecklistServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _options = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    /// <summary>A catalog of 4 printings in set "set1": collector numbers out of order and mixed
    /// numeric/alpha to exercise the natural sort, plus prices.</summary>
    private static List<SetCatalogCard> Catalog() =>
    [
        new() { GameCardId = "g10", CollectorNumber = "10", Name = "Ten",   SetCode = "set1", SetName = "Set One", Rarity = "rare",   NormalPrice = 5m,  FoilPrice = 12m, HasFoil = true },
        new() { GameCardId = "g2",  CollectorNumber = "2",  Name = "Two",   SetCode = "set1", SetName = "Set One", Rarity = "common", NormalPrice = 1m,  FoilPrice = 3m,  HasFoil = true },
        new() { GameCardId = "g1",  CollectorNumber = "1",  Name = "One",   SetCode = "set1", SetName = "Set One", Rarity = "common", NormalPrice = 1m,  FoilPrice = null, HasFoil = false },
        new() { GameCardId = "g2a", CollectorNumber = "2a", Name = "Two-A", SetCode = "set1", SetName = "Set One", Rarity = "uncommon", NormalPrice = 2m, FoilPrice = 4m, HasFoil = true },
    ];

    private SetChecklistService CreateService(IEnumerable<SetCatalogCard> catalog)
        => new([new FakeGameService(CardGame.Mtg, catalog.ToList())], new Factory(_options));

    private void AddLot(string? gameCardId, string? setCode, string? collector, int qty, bool traded = false)
    {
        using var ctx = new OmniCardDbContext(_options);
        var product = new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Single,
            Name = collector ?? "card",
            SetCode = setCode,
            GameCardId = gameCardId,
            CollectorNumber = collector,
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        ctx.Lots.Add(new InventoryLot { ProductId = product.Id, Quantity = qty, IsTraded = traded });
        ctx.SaveChanges();
    }

    [Fact]
    public async Task BuildAsync_SortsByCollectorNumber_Naturally()
    {
        var svc = CreateService(Catalog());
        var checklist = await svc.BuildAsync(CardGame.Mtg, "set1");

        Assert.Equal(["1", "2", "2a", "10"], checklist.Cards.Select(c => c.CollectorNumber).ToArray());
        Assert.Equal("Set One", checklist.SetName);
        Assert.Equal(4, checklist.TotalCount);
    }

    [Fact]
    public async Task BuildAsync_MatchesOwnershipByGameCardId_AndSumsQuantity()
    {
        AddLot(gameCardId: "g1", setCode: "set1", collector: "1", qty: 1);
        AddLot(gameCardId: "g1", setCode: "set1", collector: "1", qty: 2); // second lot, same card

        var svc = CreateService(Catalog());
        var checklist = await svc.BuildAsync(CardGame.Mtg, "set1");

        var one = checklist.Cards.Single(c => c.CollectorNumber == "1");
        Assert.True(one.Owned);
        Assert.Equal(3, one.OwnedQuantity); // 1 + 2 summed
        Assert.Equal(1, checklist.OwnedCount);       // one distinct card owned
        Assert.Equal(3, checklist.OwnedPhysicalCount);
    }

    [Fact]
    public async Task BuildAsync_FallsBackToSetAndCollector_WhenGameCardIdMissing()
    {
        // Owned lot has no GameCardId (e.g. an imported card) but matching set + collector number.
        AddLot(gameCardId: null, setCode: "SET1", collector: "2", qty: 1); // note case-insensitive set

        var svc = CreateService(Catalog());
        var checklist = await svc.BuildAsync(CardGame.Mtg, "set1");

        var two = checklist.Cards.Single(c => c.CollectorNumber == "2");
        Assert.True(two.Owned);
        Assert.Equal(1, two.OwnedQuantity);
    }

    [Fact]
    public async Task BuildAsync_ExcludesTradedLotsFromOwnership()
    {
        AddLot(gameCardId: "g1", setCode: "set1", collector: "1", qty: 1, traded: true);

        var svc = CreateService(Catalog());
        var checklist = await svc.BuildAsync(CardGame.Mtg, "set1");

        Assert.False(checklist.Cards.Single(c => c.CollectorNumber == "1").Owned);
        Assert.Equal(0, checklist.OwnedCount);
    }

    [Fact]
    public async Task BuildWantListReport_ContainsOnlyUnowned_WithPrices()
    {
        AddLot(gameCardId: "g1", setCode: "set1", collector: "1", qty: 1);

        var svc = CreateService(Catalog());
        var checklist = await svc.BuildAsync(CardGame.Mtg, "set1");
        var report = svc.BuildWantListReport(checklist);

        Assert.Equal(3, report.Rows.Count); // 4 total - 1 owned
        Assert.DoesNotContain(report.Rows, r => r.CollectorNumber == "1");
        Assert.Equal(1, report.OwnedCount);
        Assert.Equal(4, report.TotalCount);
        Assert.True(report.AnyFoil);

        var ten = report.Rows.Single(r => r.CollectorNumber == "10");
        Assert.Equal(5m, ten.NormalPrice);
        Assert.Equal(12m, ten.FoilPrice);
    }

    [Fact]
    public async Task BuildAsync_UnknownGame_Throws()
    {
        var svc = CreateService(Catalog());
        await Assert.ThrowsAsync<ArgumentException>(() => svc.BuildAsync(CardGame.Pokemon, "set1"));
    }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    private sealed class FakeGameService(CardGame game, List<SetCatalogCard> catalog) : ICardGameService
    {
        public CardGame Game => game;
        public MatchDiagnostics? LastMatchDiagnostics => null;
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) => null;
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => [];
        public List<CardMatch> GetPrintings(string cardName) => [];
        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => null;
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => [];
        public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
        public IReadOnlyList<SetInfo> GetAvailableSets() => [];
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
        public List<SetCatalogCard> GetSetCards(string setCode) => catalog.Where(c => c.SetCode == setCode).ToList();
        public object? FindCardById(string gameCardId) => null;
    }
}
