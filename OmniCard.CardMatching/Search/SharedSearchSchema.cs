namespace OmniCard.CardMatching.Search;

/// <summary>
/// The MTG-agnostic core search fields every game inherits — the ones backed by columns on
/// <c>CollectionCard</c> or handled by dedicated builders (tag/is/price/date). Mirrors the aliases in
/// <see cref="ScryfallQueryParser.NormalizeField"/>. Game schemas are built as <see cref="Default"/>
/// plus their own game-specific fields via <see cref="WithGameFields"/>.
/// </summary>
public static class SharedSearchSchema
{
    /// <summary>The core fields, in the order shown in the syntax-help popover.</summary>
    public static IReadOnlyList<SearchFieldDefinition> CoreFields { get; } =
    [
        new() { Canonical = "name", Aliases = ["name", "n"], Kind = SearchFieldKind.Core,
                Description = "Card name (also matches bare words). Use !name for an exact match.", Example = "name:bolt" },
        new() { Canonical = "set", Aliases = ["set", "s", "e", "edition"], Kind = SearchFieldKind.Core,
                Description = "Set / edition code.", Example = "set:dom" },
        new() { Canonical = "cn", Aliases = ["cn", "number"], Kind = SearchFieldKind.Core,
                Description = "Collector number.", Example = "cn:123" },
        new() { Canonical = "type", Aliases = ["type", "t"], Kind = SearchFieldKind.Core,
                Description = "Card type line.", Example = "t:creature" },
        new() { Canonical = "rarity", Aliases = ["rarity", "r"], Kind = SearchFieldKind.Core,
                SupportedOps = [ComparisonOp.Contains, ComparisonOp.Exact, ComparisonOp.NotEqual,
                    ComparisonOp.LessThan, ComparisonOp.GreaterThan, ComparisonOp.LessOrEqual, ComparisonOp.GreaterOrEqual],
                Description = "Rarity (supports <, >, <=, >= ordering).", Example = "r>=rare" },
        new() { Canonical = "color", Aliases = ["color", "c", "id", "identity", "ci", "commander"], Kind = SearchFieldKind.Core,
                Description = "Colors (WUBRG letters, or colorless/multicolor).", Example = "c:u" },
        new() { Canonical = "condition", Aliases = ["condition", "cond"], Kind = SearchFieldKind.Core,
                Description = "Card condition (NM, LP, …).", Example = "cond:nm" },
        new() { Canonical = "location", Aliases = ["location", "loc"], Kind = SearchFieldKind.Core,
                Description = "Storage location name.", Example = "loc:binder" },
        new() { Canonical = "is", Aliases = ["is", "not"], Kind = SearchFieldKind.Special,
                Description = "Boolean flags: is:foil, is:missing. Use not: to negate.", Example = "is:foil" },
        new() { Canonical = "foil", Aliases = ["foil"], Kind = SearchFieldKind.Special,
                Description = "Foil filter (foil:true / foil:false).", Example = "foil:true" },
        new() { Canonical = "tag", Aliases = ["tag", "tags"], Kind = SearchFieldKind.Special,
                Description = "User tag on the card.", Example = "tag:trade" },
        new() { Canonical = "price", Aliases = ["price"], Kind = SearchFieldKind.Special,
                Description = "Price filter.", Example = "price>=5" },
        new() { Canonical = "date", Aliases = ["date"], Kind = SearchFieldKind.Special,
                Description = "Acquisition date filter.", Example = "date>=2024-01-01" },
    ];

    /// <summary>Shared schema with only the core fields — used for MTG (whose flavor these already
    /// match) and for the "All Games" collection view.</summary>
    public static SearchSchema Default { get; } = new(CoreFields);

    /// <summary>Compose a game schema: the core fields plus the game's own fields. Game fields are
    /// appended last so their aliases win any collision (e.g. FFTCG's <c>e</c>→element beats core
    /// <c>e</c>→set) — see <see cref="SearchSchema"/>'s alias map.</summary>
    public static SearchSchema WithGameFields(IEnumerable<SearchFieldDefinition> gameFields) =>
        new(CoreFields.Concat(gameFields));
}
