using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Collection.Inventory;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Inventory;
using OmniCard.Tests.Fakes;
using OmniCard.Tests.Services.Ebay;
using OmniCard.Web.Services.TagRules;

namespace OmniCard.Tests.Web.TagRules;

public class TagRuleServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly TestDbContextFactory _factory;
    private readonly ConfigurableGameService _mtg = new();
    private readonly TagService _tags;
    private readonly TagRuleService _service;

    public TagRuleServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _factory = new TestDbContextFactory(new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options);
        using (var ctx = _factory.CreateDbContext()) ctx.Database.EnsureCreated();
        _tags = new TagService(_factory);
        _service = TagRuleServices.Create(_factory, _mtg);

        // The catalog: "dragon" is a creature, everything else an instant.
        _mtg.OnFindCardById = id => new Card
        {
            TypeLine = id == "dragon" ? "Creature — Dragon" : "Instant",
            Colors = ["R"],
        };
    }

    public void Dispose() => _conn.Dispose();

    private int Own(string gameCardId, string rarity, bool foil = false, string? cardType = null, params string[] tags)
    {
        using var ctx = _factory.CreateDbContext();
        var product = new Product
        {
            Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = gameCardId, Name = gameCardId,
            SetCode = "tst", CollectorNumber = "1", Rarity = rarity, Foil = foil, CardType = cardType,
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = product.Id, Condition = "NM" };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        if (tags.Length > 0) _tags.SetTagsForLot(lot.Id, tags);
        return lot.Id;
    }

    private TagRuleDto Rule(string query, params string[] tags) =>
        _service.Create(new TagRuleInput { Name = query, Game = "Mtg", Query = query, Tags = tags });

    [Fact]
    public void Create_RejectsInvalidRules()
    {
        var ex = Assert.Throws<TagRuleValidationException>(() =>
            _service.Create(new TagRuleInput { Name = "", Game = "Mtg", Query = "loc:binder", Tags = [] }));
        Assert.Contains(ex.Errors, e => e.Contains("name"));
        Assert.Contains(ex.Errors, e => e.Contains("tag"));
        Assert.Contains(ex.Errors, e => e.Contains("location"));

        Assert.Throws<TagRuleValidationException>(() =>
            _service.Create(new TagRuleInput { Name = "x", Game = "Nope", Query = "r:rare", Tags = ["a"] }));
    }

    [Fact]
    public void Create_CleansTags_AndUpdateReplaces()
    {
        var rule = Rule("r:rare", " Rare ", "rare", "Binder");
        Assert.Equal(["Rare", "Binder"], rule.Tags);

        var updated = _service.Update(rule.Id,
            new TagRuleInput { Name = "Mythics", Game = "Mtg", Query = "r:mythic", Tags = ["Chase"], Enabled = false });
        Assert.NotNull(updated);
        Assert.Equal("r:mythic", updated!.Query);
        Assert.False(updated.Enabled);
        Assert.Null(_service.Update(999, new TagRuleInput { Name = "x", Game = "Mtg", Query = "r:rare", Tags = ["a"] }));
        Assert.True(_service.Delete(rule.Id));
        Assert.Empty(_service.List(CardGame.Mtg));
    }

    [Fact]
    public void Preview_CountsMatchesAndCardsThatWouldChange()
    {
        Own("a", "rare");
        Own("b", "common");
        Own("c", "mythic", tags: ["Rare", "Binder"]);
        Own("d", "rare", tags: ["Rare"]);

        var preview = _service.Preview(CardGame.Mtg, "r>=rare", ["Rare", "Binder"]);

        Assert.Empty(preview.Errors);
        Assert.Equal(3, preview.MatchCount);
        Assert.Equal(2, preview.ChangeCount);
        Assert.Equal(["a", "d"], preview.Sample.Select(s => s.Name).ToList());
        Assert.Equal(["Binder"], preview.Sample.Single(s => s.Name == "d").MissingTags);
    }

    [Fact]
    public void Preview_InvalidQuery_ReturnsErrors()
    {
        var preview = _service.Preview(CardGame.Mtg, "tag:x", ["a"]);
        Assert.NotEmpty(preview.Errors);
        Assert.Equal(0, preview.MatchCount);
    }

    [Fact]
    public void Run_AddsMissingTags_KeepsExistingOnes()
    {
        var a = Own("a", "rare", tags: ["Mine"]);
        var b = Own("b", "common");
        var rule = Rule("r:rare", "Rare");

        Assert.Equal(1, _service.Run(rule.Id)!.CardsTagged);
        Assert.Equal(["Mine", "Rare"], _tags.GetTagsForLot(a));
        Assert.Empty(_tags.GetTagsForLot(b));

        Assert.Equal(0, _service.Run(rule.Id)!.CardsTagged); // already tagged: nothing to do
        Assert.Null(_service.Run(999));
    }

    [Fact]
    public void EvaluateScanItems_UsesCatalogTypeAndCopyFoil_SkipsDisabledRules()
    {
        Rule("t:creature is:foil", "Foil creature");
        Rule("r:rare", "Rare");
        var disabled = Rule("t:instant", "Instant");
        _service.Update(disabled.Id, new TagRuleInput { Name = "i", Game = "Mtg", Query = "t:instant", Tags = ["Instant"], Enabled = false });

        var results = _service.EvaluateScanItems(CardGame.Mtg,
        [
            new ScanTagRuleItem { Key = "1", GameCardId = "dragon", Name = "Dragon", Rarity = "rare", IsFoil = true },
            new ScanTagRuleItem { Key = "2", GameCardId = "dragon", Name = "Dragon", Rarity = "common" },
            new ScanTagRuleItem { Key = "3", GameCardId = "bolt", Name = "Bolt", Rarity = "common", IsFoil = true },
        ]).ToDictionary(r => r.Key, r => r.Tags);

        Assert.Equal(["Foil creature", "Rare"], results["1"]);
        Assert.Empty(results["2"]);
        Assert.Empty(results["3"]);
    }

    [Fact]
    public void ApplyToNewLots_TagsOnlyTheNewMatchingCards()
    {
        var old = Own("old", "rare");
        var fresh = Own("dragon", "rare", tags: ["Mine"]); // stored without a type: looked up from the catalog
        var other = Own("bolt", "rare");
        Rule("t:creature", "Creature");

        Assert.Equal(1, _service.ApplyToNewLots([fresh, other]));
        Assert.Equal(["Creature", "Mine"], _tags.GetTagsForLot(fresh));
        Assert.Empty(_tags.GetTagsForLot(other));
        Assert.Empty(_tags.GetTagsForLot(old));
    }
}
