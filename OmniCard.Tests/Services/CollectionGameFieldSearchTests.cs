using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.CardMatching;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

/// <summary>
/// Collection search over a game-specific field (e.g. FFTCG element:) crosses the owned-store ↔ catalog
/// DB boundary via <see cref="IGameFieldResolver"/>. These tests use a fake resolver (no real catalog
/// DB) to verify the dispatch: <see cref="CollectionQueryBuilder"/> filters owned lots by the resolved
/// GameCardId set, and <see cref="CollectionCardMatcher"/> returns the identical result (lockstep).
/// </summary>
public class CollectionGameFieldSearchTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OmniCardDbContext> _factory;

    public CollectionGameFieldSearchTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_connection).Options;
        _factory = new TestDbContextFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private void SeedCard(string gameCardId, string name, CardGame game = CardGame.FinalFantasy)
    {
        using var ctx = _factory.CreateDbContext();
        var product = new Product
        {
            Game = game, Category = ProductCategory.Single, GameCardId = gameCardId, Name = name,
            SetName = "Opus I", SetCode = "OP1", CollectorNumber = "1", Rarity = "hero",
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        ctx.Lots.Add(new InventoryLot { ProductId = product.Id, LocationId = null });
        ctx.SaveChanges();
    }

    private IReadOnlyDictionary<CardGame, ICardGameService> Services() =>
        new Dictionary<CardGame, ICardGameService> { [CardGame.FinalFantasy] = new FakeFfService() };

    [Theory]
    [InlineData("element:fire")]
    [InlineData("e:f")]
    [InlineData("element:f")]
    public void GameField_FiltersOwnedCardsByResolvedIds(string query)
    {
        SeedCard("1", "Auron");     // Fire
        SeedCard("2", "Y'shtola");  // not Fire
        SeedCard("3", "Ifrit");     // Fire

        using var ctx = _factory.CreateDbContext();
        var results = CollectionQueryBuilder
            .BuildFilteredQuery(ctx, query, CardGame.FinalFantasy, null, null, Services())
            .ToList();

        Assert.Equal(new[] { "1", "3" }, results.Select(c => c.GameCardId).OrderBy(x => x));
    }

    [Fact]
    public void GameField_Negated_ExcludesMatches()
    {
        SeedCard("1", "Auron");
        SeedCard("2", "Y'shtola");
        SeedCard("3", "Ifrit");

        using var ctx = _factory.CreateDbContext();
        var results = CollectionQueryBuilder
            .BuildFilteredQuery(ctx, "-element:fire", CardGame.FinalFantasy, null, null, Services())
            .ToList();

        Assert.Equal(["2"], results.Select(c => c.GameCardId).ToList());
    }

    [Fact]
    public void Matcher_And_QueryBuilder_AgreeForGameField()
    {
        SeedCard("1", "Auron");
        SeedCard("2", "Y'shtola");
        SeedCard("3", "Ifrit");

        using var ctx = _factory.CreateDbContext();
        // All owned cards materialized (like the binder import tray), then filtered in memory.
        var all = CollectionQueryBuilder.BuildFilteredQuery(ctx, "", CardGame.FinalFantasy, null, null).ToList();
        var sql = CollectionQueryBuilder
            .BuildFilteredQuery(ctx, "element:fire", CardGame.FinalFantasy, null, null, Services())
            .Select(c => c.GameCardId).OrderBy(x => x).ToList();

        var inMemory = CollectionCardMatcher.Filter(all, "element:fire", Services())
            .Select(c => c.GameCardId).OrderBy(x => x).ToList();

        Assert.Equal(sql, inMemory);
        Assert.Equal(new[] { "1", "3" }, inMemory);
    }
}

/// <summary>Shared test double: an FFTCG game service exposing an element field. element:fire (and the
/// f→Fire alias) resolves to card ids {"1","3"}; everything else is a no-op.</summary>
internal sealed class FakeFfService : ICardGameService, IGameFieldResolver
{
    public CardGame Game => CardGame.FinalFantasy;
    public SearchSchema SearchSchema { get; } = SharedSearchSchema.WithGameFields(
    [
        new SearchFieldDefinition
        {
            Canonical = "element", Aliases = ["element", "e", "el"], SourceKey = "Element",
            ValueAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["f"] = "Fire" },
            Description = "Element (Fire, Ice, …).", Example = "element:fire",
        },
    ]);

    public IReadOnlySet<string>? ResolveFieldCardIds(string field, ComparisonOp op, string value)
    {
        if (!SearchSchema.IsGameSpecific(field)) return null;
        var resolved = SearchSchema.ResolveValue(field, value); // f -> Fire
        return resolved.Equals("Fire", StringComparison.OrdinalIgnoreCase)
            ? new HashSet<string> { "1", "3" }
            : new HashSet<string>();
    }

    public MatchDiagnostics? LastMatchDiagnostics => null;
    public Task DownloadBulkDataAsync(IProgress<string>? p = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? p = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task ComputeImageHashesAsync(bool f = false, IProgress<string>? p = null, CancellationToken ct = default) => Task.CompletedTask;
    public CardMatch? FindClosestMatch(ulong h, ulong[]? a = null, OcrMatchResult? o = null, IReadOnlySet<string>? s = null, IReadOnlySet<string>? ps = null, int m = 14, ulong? e = null) => null;
    public List<CardMatch> SearchCards(string q, int max = 20) => [];
    public List<CardMatch> GetPrintings(string n) => [];
    public decimal? GetCurrentPrice(string id, bool f) => null;
    public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> ids, bool f) => [];
    public void RecordCorrection(ulong h, string id, ulong? a = null) { }
    public IReadOnlyList<SetInfo> GetAvailableSets() => [];
    public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> o, IProgress<string>? p = null) => Task.FromResult(new List<SetCompletionSummary>());
    public List<MissingCard> GetMissingCards(string s, IEnumerable<string> o) => [];
    public List<SetCatalogCard> GetSetCards(string s) => [];
    public object? FindCardById(string id) => null;
}
