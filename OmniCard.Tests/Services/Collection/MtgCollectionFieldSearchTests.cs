using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.CardMatching;
using OmniCard.CardMatching.Games;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Data.Catalogs;
using OmniCard.Imaging;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Inventory;
using OmniCard.Tests.Services.Ebay;

namespace OmniCard.Tests.Services.Collection;

/// <summary>
/// Owned-collection search over MTG catalog-only fields (keyword, power, legality, catalog is: flags)
/// against a real in-memory Scryfall catalog: the query builder hands the owned printings to
/// <see cref="ScryfallService"/> as candidates, which evaluates them with the exact catalog matcher.
/// </summary>
public class MtgCollectionFieldSearchTests : IDisposable
{
    private static readonly Guid Dragon = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Bear = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Unowned = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private readonly SqliteConnection _store;
    private readonly SqliteConnection _catalog;
    private readonly IDbContextFactory<OmniCardDbContext> _factory;
    private readonly DbContextOptions<ScryfallDbContext> _catalogOptions;

    public MtgCollectionFieldSearchTests()
    {
        _store = new SqliteConnection("Data Source=:memory:");
        _store.Open();
        _factory = new TestDbContextFactory(new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_store).Options);
        using (var ctx = _factory.CreateDbContext()) ctx.Database.EnsureCreated();

        _catalog = new SqliteConnection("Data Source=:memory:");
        _catalog.Open();
        _catalogOptions = new DbContextOptionsBuilder<ScryfallDbContext>().UseSqlite(_catalog).Options;
        using var cat = new ScryfallDbContext(_catalogOptions);
        cat.Database.EnsureCreated();
        cat.Cards.AddRange(
            Catalog(Dragon, "Shivan Dragon", "Legendary Creature — Dragon", "5", ["Flying"], "legal"),
            Catalog(Bear, "Grizzly Bears", "Creature — Bear", "2", [], "not_legal"),
            Catalog(Unowned, "Serra Angel", "Creature — Angel", "4", ["Flying"], "legal"));
        cat.SaveChanges();

        Own(Dragon, "Shivan Dragon");
        Own(Bear, "Grizzly Bears");
    }

    public void Dispose()
    {
        _store.Dispose();
        _catalog.Dispose();
    }

    private static Card Catalog(Guid id, string name, string type, string power, List<string> keywords, string modern) => new()
    {
        Id = id, Name = name, Lang = "en", SetCode = "tst", SetName = "Test", CollectorNumber = "1",
        Rarity = "rare", TypeLine = type, Power = power, Toughness = "1", Keywords = keywords,
        Legalities = new Dictionary<string, string> { ["modern"] = modern },
        ImageUris = new ImageUris(), Prices = new Prices(),
    };

    private void Own(Guid id, string name)
    {
        using var ctx = _factory.CreateDbContext();
        var product = new Product
        {
            Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = id.ToString(),
            Name = name, SetCode = "tst", CollectorNumber = "1", Rarity = "rare",
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        ctx.Lots.Add(new InventoryLot { ProductId = product.Id });
        ctx.SaveChanges();
    }

    private IReadOnlyDictionary<CardGame, ICardGameService> Services()
    {
        var http = new NoHttpFactory();
        var svc = new ScryfallService(
            http,
            new CatalogFactory(_catalogOptions),
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            new SetSymbolCache(http, new DataPathService(Path.GetTempPath()), NullLogger<SetSymbolCache>.Instance),
            Options.Create(new ScryfallSettings()),
            NullLogger<ScryfallService>.Instance,
            new DataPathService(Path.GetTempPath()));
        return new Dictionary<CardGame, ICardGameService> { [CardGame.Mtg] = svc };
    }

    private List<string> Search(string query)
    {
        using var ctx = _factory.CreateDbContext();
        return CollectionQueryBuilder.BuildFilteredQuery(ctx, query, CardGame.Mtg, null, null, Services())
            .Select(c => c.Name).ToList().OrderBy(n => n).ToList();
    }

    private List<string> MatchInMemory(string query)
    {
        using var ctx = _factory.CreateDbContext();
        var all = CollectionQueryBuilder.BuildFilteredQuery(ctx, "", CardGame.Mtg, null, null).ToList();
        return CollectionCardMatcher.Filter(all, query, Services(), MtgSearchSchemaPublic())
            .Select(c => c.Name).OrderBy(n => n).ToList();
    }

    private static OmniCard.CardMatching.Search.SearchSchema MtgSearchSchemaPublic() =>
        OmniCard.CardMatching.Search.MtgSearchSchema.Public;

    [Theory]
    [InlineData("kw:flying", new[] { "Shivan Dragon" })]
    [InlineData("pow>=4", new[] { "Shivan Dragon" })]
    [InlineData("f:modern", new[] { "Shivan Dragon" })]
    [InlineData("is:commander", new[] { "Shivan Dragon" })]
    [InlineData("-is:commander", new[] { "Grizzly Bears" })]
    [InlineData("is:notarealflag", new string[0])]
    [InlineData("is:nonfoil", new[] { "Grizzly Bears", "Shivan Dragon" })]
    [InlineData("-f:modern", new[] { "Grizzly Bears" })]
    [InlineData("-(kw:flying or pow>=4)", new[] { "Grizzly Bears" })]
    [InlineData("-(kw:flying -is:commander)", new[] { "Grizzly Bears", "Shivan Dragon" })]
    [InlineData("-is:notarealflag", new[] { "Grizzly Bears", "Shivan Dragon" })]
    public void CatalogFields_FilterOwnedCards(string query, string[] expected)
    {
        Assert.Equal(expected, Search(query));
        Assert.Equal(expected, MatchInMemory(query)); // in-memory matcher stays in lockstep
    }

    private sealed class NoHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class CatalogFactory(DbContextOptions<ScryfallDbContext> options) : IDbContextFactory<ScryfallDbContext>
    {
        public ScryfallDbContext CreateDbContext() => new(options);
    }
}
