using OmniCard.Collection.Lists;
using OmniCard.Shared.Lists;

namespace OmniCard.Tests.Services.Lists;

public class ListExportFormatterTests
{
    private static int _nextId;

    private static ListItemPlan Plan(int quantity, int owned, string name, string? set, string? cn,
        bool foil = false, string? foilType = null)
    {
        var item = new CardListItem
        {
            Id = ++_nextId, Quantity = quantity, GameCardId = $"{set}-{cn}", CardName = name,
            SetCode = set, CollectorNumber = cn, IsFoil = foil, FoilType = foilType,
        };
        var picks = owned == 0
            ? []
            : new List<DecklistPick> { new(1, 1, "Binder", null, null, null, null, set ?? "", cn ?? "", foil, "NM", owned, false) };
        return new ListItemPlan(item, picks);
    }

    [Fact]
    public void ToText_MatchesTheDecklistFormat()
    {
        var lines = ListExportFormatter.Lines([Plan(1, 0, "Aragorn, the Uniter", "ltr", "192")], ListExportScope.All);
        Assert.Equal("1x Aragorn, the Uniter (LTR) 192\n", ListExportFormatter.ToText(lines));
    }

    [Fact]
    public void ToText_MarksFoilAndEtched_AndOmitsMissingParts()
    {
        var lines = ListExportFormatter.Lines([
            Plan(1, 0, "Sol Ring", "c21", "263", foil: true),
            Plan(1, 0, "Sol Ring", "c21", "263", foil: true, foilType: "Etched"),
            Plan(2, 0, "Island", null, null),
        ], ListExportScope.All);
        Assert.Equal("2x Island\n1x Sol Ring (C21) 263 *F*\n1x Sol Ring (C21) 263 *E*\n", ListExportFormatter.ToText(lines));
    }

    [Fact]
    public void Lines_SplitPartlyOwnedItemsByScope()
    {
        var plan = new[] { Plan(4, 1, "Brainstorm", "ice", "61"), Plan(1, 1, "Ponder", "lrw", "79") };

        Assert.Equal([(4, "Brainstorm"), (1, "Ponder")], Pairs(ListExportFormatter.Lines(plan, ListExportScope.All)));
        Assert.Equal([(1, "Brainstorm"), (1, "Ponder")], Pairs(ListExportFormatter.Lines(plan, ListExportScope.Owned)));
        Assert.Equal([(3, "Brainstorm")], Pairs(ListExportFormatter.Lines(plan, ListExportScope.ToBuy)));
    }

    [Fact]
    public void Lines_CombineIdenticalPrintings()
    {
        var lines = ListExportFormatter.Lines(
            [Plan(1, 0, "Island", "ltr", "715"), Plan(2, 0, "island", "LTR", "715")], ListExportScope.All);
        Assert.Equal(3, Assert.Single(lines).Quantity);
    }

    [Fact]
    public void ToCsv_HasHeaderAndQuotesCommas()
    {
        var lines = ListExportFormatter.Lines([Plan(1, 0, "Aragorn, the Uniter", "ltr", "192", foil: true)], ListExportScope.All);
        var csv = ListExportFormatter.ToCsv(lines).ReplaceLineEndings("\n");
        Assert.Equal("Qty,Card Name,Set,Collector Number,Foil\n1,\"Aragorn, the Uniter\",LTR,192,Foil\n", csv);
    }

    private static List<(int, string)> Pairs(IEnumerable<ListExportLine> lines) =>
        lines.Select(l => (l.Quantity, l.CardName)).ToList();
}
