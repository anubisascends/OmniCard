using OmniCard.Models;
using OmniCard.Views.Binder;

namespace OmniCard.Tests.ViewModels;

/// <summary>Covers the reconcile rule used by the import-driven binder audit: an imported card is
/// matched to an owned unplaced copy by id (then set + collector), with foil having to agree; when
/// nothing matches, the caller creates a new card instead.</summary>
public class BinderImportMatcherTests
{
    private static CollectionCard Import(string gameCardId, string set, string number, bool foil = false) => new()
    {
        Game = CardGame.Mtg, GameCardId = gameCardId, SetCode = set, Number = number, IsFoil = foil, Condition = "NM",
    };

    private static CollectionCard Owned(int lotId, string gameCardId, string set, string number, bool foil = false) => new()
    {
        Id = lotId, Game = CardGame.Mtg, GameCardId = gameCardId, SetCode = set, Number = number, IsFoil = foil,
    };

    [Fact]
    public void FindOwnedMatch_MatchesById()
    {
        var pool = new List<CollectionCard> { Owned(1, "id-a", "SET", "1"), Owned(2, "id-b", "SET", "2") };
        var match = BinderImportMatcher.FindOwnedMatch(pool, Import("id-b", "set", "999"));
        Assert.NotNull(match);
        Assert.Equal(2, match.Id);
    }

    [Fact]
    public void FindOwnedMatch_FallsBackToSetAndCollector_CaseInsensitive()
    {
        var pool = new List<CollectionCard> { Owned(7, "", "SET", "20") };
        var match = BinderImportMatcher.FindOwnedMatch(pool, Import("", "set", "20"));
        Assert.NotNull(match);
        Assert.Equal(7, match.Id);
    }

    [Fact]
    public void FindOwnedMatch_RequiresFoilToAgree()
    {
        var pool = new List<CollectionCard> { Owned(1, "id-a", "SET", "1", foil: false) };
        // Same printing but the import is foil — must not match the non-foil owned copy.
        Assert.Null(BinderImportMatcher.FindOwnedMatch(pool, Import("id-a", "SET", "1", foil: true)));
    }

    [Fact]
    public void FindOwnedMatch_ReturnsNullWhenNothingMatches()
    {
        var pool = new List<CollectionCard> { Owned(1, "id-a", "SET", "1") };
        Assert.Null(BinderImportMatcher.FindOwnedMatch(pool, Import("id-c", "OTHER", "5")));
    }

    [Fact]
    public void ToCardMatch_CarriesPrintingIdentity()
    {
        var import = new CollectionCard
        {
            Game = CardGame.Mtg, GameCardId = "id-a", Name = "Card A", SetCode = "SET",
            SetName = "The Set", Number = "1", Rarity = "rare",
        };
        var m = BinderImportMatcher.ToCardMatch(import);
        Assert.Equal("Card A", m.Name);
        Assert.Equal("SET", m.SetCode);
        Assert.Equal("1", m.CollectorNumber);
        Assert.Equal("id-a", m.GameSpecificId);
        Assert.NotNull(m.Source);
    }
}
