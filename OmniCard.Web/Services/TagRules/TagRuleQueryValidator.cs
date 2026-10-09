using OmniCard.CardMatching.Search;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Tags;

namespace OmniCard.Web.Services.TagRules;

/// <summary>
/// Strict checks for a tag rule's search query. The search box forgives mistakes (an unknown field
/// quietly becomes a name search), but a rule writes tags to every card it matches, so a typo must be
/// an error instead. Rules may only test what a card <i>is</i> — its printing (catalog fields) and the
/// copy's foil/condition/language — not where it sits, what it's tagged or what it's worth: location
/// and tags aren't settled while a scan is being reviewed, and prices move after the tag is applied.
/// </summary>
public static class TagRuleQueryValidator
{
    private static readonly Dictionary<string, string> Disallowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["location"] = "Rules can't use location (loc:) — a scanned card's location isn't known until it's saved.",
        ["tag"] = "Rules can't use tag: — rules only look at the card itself.",
        ["price"] = "Rules can't use prices — prices change after a tag is applied.",
        ["usd"] = "Rules can't use prices — prices change after a tag is applied.",
        ["eur"] = "Rules can't use prices — prices change after a tag is applied.",
        ["tix"] = "Rules can't use prices — prices change after a tag is applied.",
        ["date"] = "Rules can't use date: — use year: for a printing's release year.",
    };

    private static readonly HashSet<string> Directives = new(StringComparer.OrdinalIgnoreCase)
        { "order", "direction", "unique", "display", "prefer" };

    /// <summary>The copy's own is: flags (from the lot). is:missing / is:missingdb are bookkeeping flags,
    /// not card properties, so rules can't use them.</summary>
    private static readonly HashSet<string> OwnedIsFlags = new(StringComparer.OrdinalIgnoreCase) { "foil", "nonfoil" };

    /// <summary>Problems with <paramref name="query"/> for a rule of <paramref name="game"/>, parsed with
    /// that game's <paramref name="schema"/>. Empty = valid.</summary>
    public static IReadOnlyList<string> Validate(CardGame game, string? query, SearchSchema schema)
    {
        if (string.IsNullOrWhiteSpace(query))
            return ["Enter a search query."];
        if (query.Length > TagRule.MaxQueryLength)
            return [$"The query is too long (max {TagRule.MaxQueryLength} characters)."];

        var node = ScryfallQueryParser.ParseFilter(query, schema);
        if (node is null)
            return ["Enter a search query."];

        var errors = new List<string>();
        foreach (var f in Fields(node))
        {
            var error = Check(game, f, schema);
            if (error is not null && !errors.Contains(error)) errors.Add(error);
        }
        return errors;
    }

    private static string? Check(CardGame game, FieldFilter f, SearchSchema schema)
    {
        if (Disallowed.TryGetValue(f.Field, out var why)) return why;
        if (Directives.Contains(f.Field)) return $"'{f.Field}:' sorts results; it isn't allowed in a rule.";
        if (string.IsNullOrWhiteSpace(f.Value)) return $"'{f.Field}:' needs a value.";

        if (f.Field == "is")
        {
            var v = f.Value.Trim();
            if (OwnedIsFlags.Contains(v)) return null;
            if (v.Equals("missing", StringComparison.OrdinalIgnoreCase) || v.Equals("missingdb", StringComparison.OrdinalIgnoreCase))
                return $"Rules can't use is:{v}.";
            return game == CardGame.Mtg && ScryfallCardFilter.IsFlags.Contains(v) ? null : $"Unknown flag 'is:{v}'.";
        }

        // Bare words parse as name searches; anything else must be a field this game declares.
        return f.Field == "name" || schema.Find(f.Field) is not null ? null : $"Unknown field '{f.Field}'.";
    }

    private static IEnumerable<FieldFilter> Fields(FilterNode node) => node switch
    {
        FieldFilter f => [f],
        AndFilter a => a.Children.SelectMany(Fields),
        OrFilter o => o.Children.SelectMany(Fields),
        NotFilter n => Fields(n.Inner),
        _ => [],
    };
}
