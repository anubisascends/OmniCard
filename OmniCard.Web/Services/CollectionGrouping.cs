using System.Text.Json;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Collection;
using OmniCard.Web.Api.Mapping;

namespace OmniCard.Web.Services;

/// <summary>
/// Nested row grouping for the Collection / Location card table. The whole filtered set is grouped by
/// one or more columns (outermost first) into a tree, then flattened into one row sequence — a header
/// row per group followed by its subgroups or, at the innermost level, its cards — and that sequence
/// is paged. Doing it server-side over every card keeps group counts/values true and groups whole,
/// which a page-local grouping in the grid couldn't.
///
/// <para>Each group opens expanded unless <c>collapsedByDefault</c>; the ids in <c>toggled</c> flip
/// that default for individual groups. A collapsed group still shows its header (with its totals) but
/// none of its rows. A page that starts mid-group repeats the enclosing headers at its top, marked
/// <see cref="CardGroupDto.Continued"/>, so the reader always sees which group the rows belong to.</para>
/// </summary>
public static class CollectionGrouping
{
    /// <summary>Groupable columns, named as the client's grid fields, in menu order.</summary>
    public static readonly IReadOnlyList<string> Fields =
        ["setCode", "rarity", "condition", "language", "isFoil", "game", "containerName", "listingStatus"];

    // Grade order for condition groups; anything else follows alphabetically.
    private static readonly string[] ConditionOrder = ["M", "NM", "LP", "MP", "HP", "DMG"];

    /// <summary>Known group fields from <paramref name="fields"/> (case-insensitive), canonically named,
    /// de-duplicated, in the given order.</summary>
    public static List<string> Canonicalize(IEnumerable<string?>? fields) =>
        (fields ?? [])
            .Select(f => Fields.FirstOrDefault(k => string.Equals(k, f?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OfType<string>()
            .Distinct()
            .ToList();

    /// <summary>One row of the flattened sequence: a group header or a card.</summary>
    public sealed record Row(CardGroupDto? Group, CollectionCard? Card);

    private sealed class Node
    {
        public required string Id;
        public required int Level;
        public required string Field;
        public required string Key;
        public required string Label;
        public int Rows;
        public int Quantity;
        public decimal Value;
        public List<Node>? Children;
        public List<CollectionCard> Cards = [];

        public CardGroupDto ToDto(bool collapsed, bool continued) => new()
        {
            Id = Id, Level = Level, Field = Field, Key = Key, Label = Label,
            Rows = Rows, Quantity = Quantity, Value = Value, Collapsed = collapsed, Continued = continued,
        };
    }

    /// <summary>
    /// Group <paramref name="lots"/> by <paramref name="groupBy"/> and return one page of the flattened
    /// rows plus the total row count. <paramref name="arrangeLeaf"/> turns an innermost group's lots into
    /// its grid rows (stacking + sorting). Market prices (and listing status, when grouping by it) must
    /// already be on the lots. When the sort column is also a group column, those groups follow the sort
    /// direction; otherwise groups run A→Z with "none" last.
    /// </summary>
    public static (int Total, List<Row> Rows) Page(
        IReadOnlyList<CollectionCard> lots,
        IReadOnlyList<string> groupBy,
        Func<IEnumerable<CollectionCard>, List<CollectionCard>> arrangeLeaf,
        bool collapsedByDefault,
        IReadOnlySet<string> toggled,
        int skip,
        int take,
        string? sort = null,
        bool desc = false)
    {
        if (groupBy.Count == 0)
            throw new ArgumentException("At least one group column is required.", nameof(groupBy));

        var tree = Build(lots, groupBy, 0, [], arrangeLeaf, sort, desc);

        // Flatten, remembering each row's enclosing header so a page can repeat it.
        var rows = new List<Row>();
        var parents = new List<int>();
        void Flatten(List<Node> nodes, int parent)
        {
            foreach (var n in nodes)
            {
                var collapsed = collapsedByDefault ^ toggled.Contains(n.Id);
                var index = rows.Count;
                rows.Add(new Row(n.ToDto(collapsed, continued: false), null));
                parents.Add(parent);
                if (collapsed) continue;
                if (n.Children is not null)
                    Flatten(n.Children, index);
                else
                    foreach (var card in n.Cards)
                    {
                        rows.Add(new Row(null, card));
                        parents.Add(index);
                    }
            }
        }
        Flatten(tree, -1);

        var total = rows.Count;
        if (skip >= total)
            return (total, []);

        var page = rows.Skip(skip).Take(take).ToList();
        var continued = new List<Row>();
        for (var p = parents[skip]; p >= 0; p = parents[p])
            continued.Insert(0, rows[p] with { Group = rows[p].Group! with { Continued = true } });
        return (total, [.. continued, .. page]);
    }

    private static List<Node> Build(
        IEnumerable<CollectionCard> lots, IReadOnlyList<string> groupBy, int level, List<string> path,
        Func<IEnumerable<CollectionCard>, List<CollectionCard>> arrangeLeaf, string? sort, bool desc)
    {
        var field = groupBy[level];
        var reverse = desc && string.Equals(sort, field, StringComparison.OrdinalIgnoreCase);
        var groups = lots
            .Select(c => (Card: c, Group: KeyOf(field, c)))
            .GroupBy(x => x.Group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Members: g.Select(x => x.Card).ToList(), First: g.First().Group))
            .ToList();

        var ordered = groups
            .OrderBy(g => g.First.Key.Length == 0 ? 1 : 0) // "none" stays last either way
            .ThenBy(g => reverse ? -g.First.Rank : g.First.Rank);
        var sorted = reverse
            ? ordered.ThenByDescending(g => g.First.Label, StringComparer.OrdinalIgnoreCase)
            : ordered.ThenBy(g => g.First.Label, StringComparer.OrdinalIgnoreCase);

        var nodes = new List<Node>();
        foreach (var (members, first) in sorted)
        {
            List<string> keyPath = [.. path, first.Key];
            // Totals come from the lots before arranging: stacking folds quantities onto one representative.
            var node = new Node
            {
                Id = JsonSerializer.Serialize(keyPath),
                Level = level,
                Field = field,
                Key = first.Key,
                Label = first.Label,
                Quantity = members.Sum(c => c.Quantity),
                Value = members.Sum(c => c.MarketPrice * c.Quantity),
            };
            if (level + 1 < groupBy.Count)
            {
                node.Children = Build(members, groupBy, level + 1, keyPath, arrangeLeaf, sort, desc);
                node.Rows = node.Children.Sum(c => c.Rows);
            }
            else
            {
                node.Cards = arrangeLeaf(members);
                node.Rows = node.Cards.Count;
            }
            nodes.Add(node);
        }
        return nodes;
    }

    /// <summary>A card's group for one column: raw key, display label, and an ordering rank that
    /// precedes the label (grade order for condition, non-foil before foil, English first…).</summary>
    private static (string Key, string Label, int Rank) KeyOf(string field, CollectionCard c)
    {
        switch (field)
        {
            case "setCode":
                var code = c.SetCode ?? "";
                return (code, string.IsNullOrWhiteSpace(c.SetName) || code.Length == 0 ? code : $"{c.SetName} ({code})", 0);
            case "rarity":
                return (c.Rarity ?? "", c.Rarity ?? "", 0);
            case "condition":
                var condition = c.Condition ?? "";
                var grade = Array.FindIndex(ConditionOrder, g => string.Equals(g, condition, StringComparison.OrdinalIgnoreCase));
                return (condition, condition, grade < 0 ? ConditionOrder.Length : grade);
            case "language":
                var language = string.IsNullOrEmpty(c.Language) ? "en" : c.Language;
                return (language, language, language == "en" ? 0 : 1);
            case "isFoil":
                return c.IsFoil ? ("true", "true", 1) : ("false", "false", 0);
            case "game":
                return (DtoMapping.GameId(c.Game), DtoMapping.GameDisplayName(c.Game), 0);
            case "containerName":
                return c.ContainerId is int id ? (id.ToString(), c.Container?.Name ?? "", 0) : ("", "", 0);
            case "listingStatus":
                return c.ListingStatus is { } status ? (status.ToString(), status.ToString(), (int)status) : ("", "", 0);
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "Not a groupable column.");
        }
    }
}
