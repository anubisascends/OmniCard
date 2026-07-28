using OmniCard.Collection;
using OmniCard.Models;
using OmniCard.Tests.Fakes;
using Xunit;

namespace OmniCard.Tests.Services;

public class DecklistPrintingResolverTests
{
    private static CardMatch Match(string id, string set, string cn) =>
        new() { GameSpecificId = id, Name = "Island", SetCode = set, CollectorNumber = cn };

    [Fact]
    public void SetAndCollector_ExactHit_Returned()
    {
        var gs = new ConfigurableGameService
        {
            OnSearchCards = (q, _) => [Match("a", "SCD", "337"), Match("b", "SCD", "999")],
        };
        var result = DecklistPrintingResolver.Resolve(gs, new DecklistEntry(4, "Island", "SCD", "337"));
        Assert.Equal("a", result!.GameSpecificId);
    }

    [Fact]
    public void SetAndCollector_NoExactMatch_Unresolved()
    {
        var gs = new ConfigurableGameService { OnSearchCards = (_, _) => [Match("b", "SCD", "999")] };
        var result = DecklistPrintingResolver.Resolve(gs, new DecklistEntry(1, "Island", "SCD", "4"));
        Assert.Null(result);
    }

    [Fact]
    public void SetOnly_PicksCheapestInSet()
    {
        var gs = new ConfigurableGameService
        {
            OnGetPrintings = _ => [Match("a", "SCD", "337"), Match("b", "SCD", "338"), Match("c", "OTHER", "1")],
            OnGetCurrentPrices = (_, _) => new() { ["a"] = 2m, ["b"] = 1m, ["c"] = 0.1m },
        };
        var result = DecklistPrintingResolver.Resolve(gs, new DecklistEntry(1, "Island", "SCD", null));
        Assert.Equal("b", result!.GameSpecificId);   // cheapest within SCD, not the cheaper OTHER
    }

    [Fact]
    public void CollectorOnly_PicksCheapestAcrossSetsWithThatNumber()
    {
        var gs = new ConfigurableGameService
        {
            OnGetPrintings = _ => [Match("a", "SCD", "5"), Match("b", "XYZ", "5"), Match("c", "SCD", "6")],
            OnGetCurrentPrices = (_, _) => new() { ["a"] = 3m, ["b"] = 1m },
        };
        var result = DecklistPrintingResolver.Resolve(gs, new DecklistEntry(1, "Island", null, "5"));
        Assert.Equal("b", result!.GameSpecificId);
    }

    [Fact]
    public void NameOnly_PicksCheapestPrinting()
    {
        var gs = new ConfigurableGameService
        {
            OnGetPrintings = _ => [Match("a", "SCD", "1"), Match("b", "XYZ", "2")],
            OnGetCurrentPrices = (_, _) => new() { ["a"] = 5m, ["b"] = 2m },
        };
        var result = DecklistPrintingResolver.Resolve(gs, new DecklistEntry(1, "Island", null, null));
        Assert.Equal("b", result!.GameSpecificId);
    }

    [Fact]
    public void NameOnly_NoPrintings_Unresolved()
    {
        var gs = new ConfigurableGameService { OnGetPrintings = _ => [] };
        var result = DecklistPrintingResolver.Resolve(gs, new DecklistEntry(1, "Nonesuch", null, null));
        Assert.Null(result);
    }

    [Fact]
    public void NameOnly_NoPrices_FallsBackToFirstPrinting()
    {
        var gs = new ConfigurableGameService
        {
            OnGetPrintings = _ => [Match("a", "SCD", "1"), Match("b", "XYZ", "2")],
            OnGetCurrentPrices = (_, _) => new(),   // nothing priced
        };
        var result = DecklistPrintingResolver.Resolve(gs, new DecklistEntry(1, "Island", null, null));
        Assert.Equal("a", result!.GameSpecificId);
    }
}
