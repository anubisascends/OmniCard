using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Storage;
using OmniCard.Web.Api.Controllers;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>
/// Guards the Collection / Location table's nested row grouping: groups are built over the whole set
/// (true totals, never split by paging), collapse/expand flips per group, and a page that starts
/// mid-group repeats its enclosing headers.
/// </summary>
public class CollectionGroupingTests
{
    private static int _nextId = 1;

    private static CollectionCard Card(
        string name, string set = "AAA", string rarity = "common", string condition = "NM",
        bool foil = false, int qty = 1, decimal price = 1m, string number = "1")
        => new()
        {
            Id = _nextId++,
            Game = CardGame.Mtg,
            Name = name,
            SetCode = set,
            SetName = $"Set {set}",
            Number = number,
            Rarity = rarity,
            Condition = condition,
            Language = "en",
            IsFoil = foil,
            Quantity = qty,
            MarketPrice = price,
        };

    private static List<CollectionCard> Flat(IEnumerable<CollectionCard> lots) =>
        CollectionController.SortRows(lots.Select(c => { c.StackedIds = [c.Id]; return c; }), "name", false);

    private static (int Total, List<CollectionGrouping.Row> Rows) Page(
        IReadOnlyList<CollectionCard> lots, string[] groupBy, int skip = 0, int take = 100,
        bool collapsed = false, string[]? toggled = null,
        Func<IEnumerable<CollectionCard>, List<CollectionCard>>? arrange = null, string? sort = null, bool desc = false)
        => CollectionGrouping.Page(lots, groupBy, arrange ?? Flat, collapsed,
            (toggled ?? []).ToHashSet(), skip, take, sort, desc);

    private static string Describe(CollectionGrouping.Row r) =>
        r.Group is { } g ? $"{new string('>', g.Level + 1)}{g.Key}{(g.Continued ? "*" : "")}" : r.Card!.Name;

    [Fact]
    public void SingleLevel_HeadersFollowedByTheirCards_WithTotals()
    {
        var lots = new List<CollectionCard>
        {
            Card("Bolt", set: "BBB", qty: 2, price: 3m),
            Card("Ape", set: "AAA", price: 1.5m),
            Card("Cat", set: "BBB", price: 1m),
        };

        var (total, rows) = Page(lots, ["setCode"]);

        Assert.Equal(5, total);
        Assert.Equal([">AAA", "Ape", ">BBB", "Bolt", "Cat"], rows.Select(Describe));
        var bbb = rows[2].Group!;
        Assert.Equal("Set BBB (BBB)", bbb.Label);
        Assert.Equal(2, bbb.Rows);
        Assert.Equal(3, bbb.Quantity);
        Assert.Equal(7m, bbb.Value);
    }

    [Fact]
    public void Nested_GroupsWithinGroups()
    {
        var lots = new List<CollectionCard>
        {
            Card("A", set: "AAA", rarity: "rare"),
            Card("B", set: "AAA", rarity: "common"),
            Card("C", set: "BBB", rarity: "rare"),
        };

        var (_, rows) = Page(lots, ["setCode", "rarity"]);

        Assert.Equal([">AAA", ">>common", "B", ">>rare", "A", ">BBB", ">>rare", "C"], rows.Select(Describe));
        Assert.Equal(2, rows[0].Group!.Rows);
        Assert.Equal("[\"AAA\",\"rare\"]", rows[3].Group!.Id);
    }

    [Fact]
    public void CollapsedGroup_KeepsHeaderAndTotals_HidesRows()
    {
        var lots = new List<CollectionCard> { Card("A", set: "AAA"), Card("B", set: "BBB"), Card("C", set: "BBB") };

        var (total, rows) = Page(lots, ["setCode"], toggled: ["[\"BBB\"]"]);

        Assert.Equal(3, total);
        Assert.Equal([">AAA", "A", ">BBB"], rows.Select(Describe));
        Assert.True(rows[2].Group!.Collapsed);
        Assert.Equal(2, rows[2].Group!.Rows);
    }

    [Fact]
    public void CollapseAll_ThenToggleOne_ExpandsOnlyThatGroup()
    {
        var lots = new List<CollectionCard>
        {
            Card("A", set: "AAA", rarity: "rare"),
            Card("B", set: "BBB", rarity: "rare"),
        };

        var (_, rows) = Page(lots, ["setCode", "rarity"], collapsed: true, toggled: ["[\"AAA\"]"]);

        // AAA is expanded, but its subgroups keep the collapsed default.
        Assert.Equal([">AAA", ">>rare", ">BBB"], rows.Select(Describe));
        Assert.False(rows[0].Group!.Collapsed);
        Assert.True(rows[1].Group!.Collapsed);
    }

    [Fact]
    public void PageStartingMidGroup_RepeatsEnclosingHeaders()
    {
        var lots = new List<CollectionCard>
        {
            Card("A", set: "AAA", rarity: "rare"),
            Card("B", set: "AAA", rarity: "rare"),
            Card("C", set: "AAA", rarity: "rare"),
        };

        // Flattened: >AAA, >>rare, A, B, C — page 2 (size 3) starts at "B".
        var (total, rows) = Page(lots, ["setCode", "rarity"], skip: 3, take: 3);

        Assert.Equal(5, total);
        Assert.Equal([">AAA*", ">>rare*", "B", "C"], rows.Select(Describe));
    }

    [Fact]
    public void EmptyKey_SortsLast_AndSortDescOnGroupColumnReversesGroups()
    {
        var lots = new List<CollectionCard>
        {
            Card("A", rarity: ""),
            Card("B", rarity: "common"),
            Card("C", rarity: "rare"),
        };

        var (_, asc) = Page(lots, ["rarity"]);
        var (_, desc) = Page(lots, ["rarity"], sort: "rarity", desc: true);

        Assert.Equal([">common", ">rare", ">"], asc.Where(r => r.Group is not null).Select(Describe));
        Assert.Equal([">rare", ">common", ">"], desc.Where(r => r.Group is not null).Select(Describe));
    }

    [Fact]
    public void Condition_FollowsGradeOrder_FoilAfterNonFoil()
    {
        var lots = new List<CollectionCard>
        {
            Card("A", condition: "DMG"), Card("B", condition: "LP"), Card("C", condition: "NM", foil: true),
        };

        var (_, byCondition) = Page(lots, ["condition"]);
        var (_, byFoil) = Page(lots, ["isFoil"]);

        Assert.Equal([">NM", ">LP", ">DMG"], byCondition.Where(r => r.Group is not null).Select(Describe));
        Assert.Equal([">false", ">true"], byFoil.Where(r => r.Group is not null).Select(Describe));
    }

    [Fact]
    public void Stacking_AppliesWithinGroups_TotalsCountCopies()
    {
        // Two NM copies and one LP copy of the same printing: grouped by condition they must not
        // stack across groups, and the header totals use the lots, not the stacked representative.
        var lots = new List<CollectionCard>
        {
            Card("Bolt", condition: "NM", qty: 1, price: 2m),
            Card("Bolt", condition: "NM", qty: 2, price: 2m),
            Card("Bolt", condition: "LP", qty: 1, price: 2m),
        };

        var (_, rows) = Page(lots, ["condition"],
            arrange: leaf => CollectionController.SortRows(CollectionController.StackRows(leaf), "name", false));

        Assert.Equal([">NM", "Bolt", ">LP", "Bolt"], rows.Select(Describe));
        Assert.Equal(1, rows[0].Group!.Rows);
        Assert.Equal(3, rows[0].Group!.Quantity);
        Assert.Equal(6m, rows[0].Group!.Value);
        Assert.Equal(3, rows[1].Card!.Quantity);
    }

    [Fact]
    public void LocationAndListingStatus_KeyByIdAndStatus()
    {
        var binder = new StorageContainer { Id = 7, Name = "Binder" };
        var a = Card("A"); a.ContainerId = 7; a.Container = binder;
        var b = Card("B"); b.ListingStatus = ListingStatus.Listed;

        var (_, byLocation) = Page([a, b], ["containerName"]);
        var (_, byStatus) = Page([a, b], ["listingStatus"]);

        Assert.Equal("7", byLocation[0].Group!.Key);
        Assert.Equal("Binder", byLocation[0].Group!.Label);
        Assert.Equal("", byLocation[2].Group!.Key); // unlocated last
        Assert.Equal([">Listed", ">"], byStatus.Where(r => r.Group is not null).Select(Describe));
    }

    [Fact]
    public void Canonicalize_KeepsKnownFields_InOrder_CaseInsensitive()
    {
        Assert.Equal(["rarity", "setCode"], CollectionGrouping.Canonicalize(["RARITY", "bogus", "setcode", "rarity", null]));
    }
}
