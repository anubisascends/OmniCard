namespace OmniCard.CardMatching;

/// <summary>
/// Where a search field's data lives, which decides how it is filtered.
/// </summary>
public enum SearchFieldKind
{
    /// <summary>Maps to a column on <c>CollectionCard</c> and is filterable in SQL by the shared
    /// <c>CollectionQueryBuilder</c> (name/set/cn/rarity/color/type/condition/location).</summary>
    Core,

    /// <summary>Only exists in the per-game catalog DB (Element, Cost, HP, ATK…). Collection
    /// filtering must go through the game service's id-resolution path (<see cref="IGameFieldResolver"/>).</summary>
    GameSpecific,

    /// <summary>Lives outside both and is handled by a dedicated builder (tag/is/foil/price/date).</summary>
    Special,
}

/// <summary>
/// Declares one searchable field for a game: its canonical name, the aliases users may type
/// (e.g. <c>element</c>/<c>e</c>), optional value aliases (<c>f</c>→<c>Fire</c>), the operators it
/// supports, and UI hint text. The <see cref="SourceKey"/> tells the owning game service which
/// catalog property (or TCGCSV extendedData display name) backs the field; nothing outside the game
/// service interprets it.
/// </summary>
public sealed record SearchFieldDefinition
{
    /// <summary>Canonical field name the parser resolves aliases to (lower-case), e.g. "element".</summary>
    public required string Canonical { get; init; }

    /// <summary>All tokens (including <see cref="Canonical"/>) that resolve to this field, lower-case.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Case-insensitive value shorthands, e.g. {"f":"Fire","i":"Ice"}. Empty = no aliasing.</summary>
    public IReadOnlyDictionary<string, string> ValueAliases { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Operators this field accepts (drives UI hints; the resolver enforces semantics).</summary>
    public IReadOnlyList<ComparisonOp> SupportedOps { get; init; } =
        [ComparisonOp.Contains, ComparisonOp.Exact, ComparisonOp.NotEqual];

    public SearchFieldKind Kind { get; init; } = SearchFieldKind.GameSpecific;

    /// <summary>One-line human description for the syntax help popover.</summary>
    public string Description { get; init; } = "";

    /// <summary>A copy-pasteable example query for this field, e.g. "element:fire".</summary>
    public string Example { get; init; } = "";

    /// <summary>Catalog property name (column-backed games) or extendedData display name (TCGCSV blob).
    /// Consumed only by the owning game service. Null for <see cref="SearchFieldKind.Core"/>/<see cref="SearchFieldKind.Special"/>
    /// fields that the shared builders already know how to handle.</summary>
    public string? SourceKey { get; init; }
}
