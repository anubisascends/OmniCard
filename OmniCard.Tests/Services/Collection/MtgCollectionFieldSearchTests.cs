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
    private static readonly Guid Fountain = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");

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
            Catalog(Dragon, "Shivan Dragon", "Legendary Creature — Dragon", "5", ["Flying"], "legal", ["R"]),
            Catalog(Bear, "Grizzly Bears", "Creature — Bear", "2", [], "not_legal", ["G"]),
            Catalog(Unowned, "Serra Angel", "Creature — Angel", "4", ["Flying"], "legal", ["W"]),
            // A dual land: no colours (so the owned Color column holds the "Land" bucket), W/U identity.
            Catalog(Fountain, "Hallowed Fountain", "Land — Plains Island", "", [], "not_legal", ["W", "U"], colors: []));
        cat.SaveChanges();

        Own(Dragon, "Shivan Dragon", "R", "Creature");
        Own(Bear, "Grizzly Bears", "G", "Creature");
        Own(Fountain, "Hallowed Fountain", "Land", "Land");
    }

    public void Dispose()
    {
        _store.Dispose();
        _catalog.Dispose();
    }

    private static Card Catalog(Guid id, string name, string type, string power, List<string> keywords, string modern,
        List<string> identity, List<string>? colors = null) => new()
    {
        Id = id, Name = name, Lang = "en", SetCode = "tst", SetName = "Test", CollectorNumber = "1",
        Rarity = "rare", TypeLine = type, Power = power, Toughness = "1", Keywords = keywords,
        Colors = colors ?? identity, ColorIdentity = identity,
        Legalities = new Dictionary<string, string> { ["modern"] = modern },
        ImageUris = new ImageUris(), Prices = new Prices(),
    };

    private void Own(Guid id, string name, string color, string cardType)
    {
        using var ctx = _factory.CreateDbContext();
        var product = new Product
        {
            Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = id.ToString(),
            Name = name, SetCode = "tst", CollectorNumber = "1", Rarity = "rare", Color = color, CardType = cardType,
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
    [InlineData("-is:commander", new[] { "Grizzly Bears", "Hallowed Fountain" })]
    [InlineData("is:notarealflag", new string[0])]
    [InlineData("is:nonfoil", new[] { "Grizzly Bears", "Hallowed Fountain", "Shivan Dragon" })]
    [InlineData("-f:modern", new[] { "Grizzly Bears", "Hallowed Fountain" })]
    [InlineData("-(kw:flying or pow>=4)", new[] { "Grizzly Bears", "Hallowed Fountain" })]
    [InlineData("-(kw:flying -is:commander)", new[] { "Grizzly Bears", "Hallowed Fountain", "Shivan Dragon" })]
    [InlineData("-is:notarealflag", new[] { "Grizzly Bears", "Hallowed Fountain", "Shivan Dragon" })]
    public void CatalogFields_FilterOwnedCards(string query, string[] expected)
    {
        Assert.Equal(expected, Search(query));
        Assert.Equal(expected, MatchInMemory(query)); // in-memory matcher stays in lockstep
    }

    // id: is colour identity (catalog), not the owned Color column — a dual land is colourless but
    // has a two-colour identity. c: keeps the owned-colour meaning.
    [Theory]
    [InlineData("t:land id:multi", new[] { "Hallowed Fountain" })]
    [InlineData("t:land id:m", new[] { "Hallowed Fountain" })]
    [InlineData("t:land c:multi", new string[0])]
    [InlineData("id:wu", new[] { "Hallowed Fountain" })]
    [InlineData("ci=r", new[] { "Shivan Dragon" })]
    [InlineData("id<=wug", new[] { "Grizzly Bears", "Hallowed Fountain" })]
    [InlineData("identity>=2", new[] { "Hallowed Fountain" })]
    [InlineData("-id:multi", new[] { "Grizzly Bears", "Shivan Dragon" })]
    [InlineData("id:multi or c:r", new[] { "Hallowed Fountain", "Shivan Dragon" })]
    public void ColorIdentity_UsesCatalogIdentity(string query, string[] expected)
    {
        Assert.Equal(expected, Search(query));
        Assert.Equal(expected, MatchInMemory(query));
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
