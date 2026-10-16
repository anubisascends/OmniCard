using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Tests.Fakes;
using OmniCard.Tests.Services.Ebay;
using OmniCard.Web.Services.ScanValidation;

namespace OmniCard.Tests.Web.ScanValidation;

public class ScanValidationServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly TestDbContextFactory _factory;
    private readonly ConfigurableGameService _mtg = new();
    private readonly ScanValidationService _service;

    public ScanValidationServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _factory = new TestDbContextFactory(new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options);
        using (var ctx = _factory.CreateDbContext()) ctx.Database.EnsureCreated();
        _service = new ScanValidationService(_factory, [_mtg], TimeProvider.System);

        // The catalog: "dragon" is a creature, everything else an instant.
        _mtg.OnFindCardById = id => new Card
        {
            TypeLine = id == "dragon" ? "Creature — Dragon" : "Instant",
            Colors = ["R"],
        };
    }

    public void Dispose() => _conn.Dispose();

    private static ScanTagRuleItem Item(string key, string gameCardId, string setCode = "dom", bool foil = false,
        params string[] tags) =>
        new() { Key = key, GameCardId = gameCardId, Name = gameCardId, SetCode = setCode, Rarity = "rare", IsFoil = foil, Tags = tags };

    [Fact]
    public void Check_ReturnsTheCardsThatDontMatch()
    {
        var result = _service.Check(CardGame.Mtg, "set:dom t:creature",
        [
            Item("1", "dragon"),
            Item("2", "bolt"),               // instant (from the catalog)
            Item("3", "dragon", setCode: "m19"),
        ]);

        Assert.Empty(result.Errors);
        Assert.Equal(["2", "3"], result.FailedKeys);
    }

    [Fact]
    public void Check_FailsScansWithNoIdentity()
    {
        var result = _service.Check(CardGame.Mtg, "set:dom", [Item("1", "dragon"), Item("2", "")]);
        Assert.Equal(["2"], result.FailedKeys);
    }

    [Fact]
    public void Check_UsesTheCopysFoilAndTags()
    {
        var result = _service.Check(CardGame.Mtg, "is:foil tag:binder",
        [
            Item("1", "dragon", foil: true, tags: "Binder"),
            Item("2", "dragon", foil: true),
            Item("3", "dragon", tags: "Binder"),
        ]);
        Assert.Equal(["2", "3"], result.FailedKeys);
    }

    [Fact]
    public void Check_InvalidQuery_ReturnsErrorsAndChecksNothing()
    {
        var result = _service.Check(CardGame.Mtg, "loc:binder bogus:1", [Item("1", "dragon")]);
        Assert.Empty(result.FailedKeys);
        Assert.Contains(result.Errors, e => e.Contains("location"));
        Assert.Contains(result.Errors, e => e.Contains("bogus"));

        Assert.NotEmpty(_service.Check(CardGame.Mtg, "  ", [Item("1", "dragon")]).Errors);
        Assert.NotEmpty(_service.Check(CardGame.Mtg, "usd>1", [Item("1", "dragon")]).Errors);
    }

    [Fact]
    public void Save_ReplacesARuleOfTheSameNamePerGame_AndDeletes()
    {
        var first = _service.Save(new ScanValidationRuleInput { Game = "Mtg", Name = " Dominaria ", Query = "set:dom" });
        Assert.Equal("Dominaria", first.Name);

        var replaced = _service.Save(new ScanValidationRuleInput { Game = "mtg", Name = "dominaria", Query = " set:dom is:foil " });
        Assert.Equal(first.Id, replaced.Id);
        Assert.Equal("set:dom is:foil", replaced.Query);

        _service.Save(new ScanValidationRuleInput { Game = "Mtg", Name = "Creatures", Query = "t:creature" });
        Assert.Equal(["Creatures", "dominaria"], _service.List(CardGame.Mtg).Select(r => r.Name));
        Assert.Empty(_service.List(CardGame.Pokemon));

        Assert.True(_service.Delete(first.Id));
        Assert.False(_service.Delete(first.Id));
        Assert.Single(_service.List(CardGame.Mtg));
    }

    [Fact]
    public void Save_RejectsBadInput()
    {
        var ex = Assert.Throws<ScanValidationRuleException>(() =>
            _service.Save(new ScanValidationRuleInput { Game = "Mtg", Name = "", Query = "tag:" }));
        Assert.Contains(ex.Errors, e => e.Contains("name"));
        Assert.Contains(ex.Errors, e => e.Contains("tag:"));

        Assert.Throws<ScanValidationRuleException>(() =>
            _service.Save(new ScanValidationRuleInput { Game = "Nope", Name = "x", Query = "set:dom" }));
    }
}
