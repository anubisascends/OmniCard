namespace OmniCard.CardMatching.Search;

/// <summary>
/// Magic: The Gathering's Scryfall-style search vocabulary. Two schemas are exposed because MTG is
/// searched two ways:
/// <list type="bullet">
/// <item><see cref="Catalog"/> — used by <c>ScryfallService.SearchCards</c> against the full Scryfall
/// catalog. It splits <c>c:</c> (colours) from <c>id:</c> (colour identity) as Scryfall does.</item>
/// <item><see cref="Public"/> — exposed via <c>IGameFieldResolver.SearchSchema</c> for the syntax-help
/// popover and owned-collection search. It keeps the shared core's merged <c>color</c> field (so the
/// existing single-column collection colour filter keeps working) and adds the catalog-only fields as
/// game-specific so they route through the collection's id-resolution path.</item>
/// </list>
/// </summary>
public static class MtgSearchSchema
{
    private static readonly ComparisonOp[] AllOps =
    [
        ComparisonOp.Contains, ComparisonOp.Exact, ComparisonOp.NotEqual,
        ComparisonOp.LessThan, ComparisonOp.GreaterThan, ComparisonOp.LessOrEqual, ComparisonOp.GreaterOrEqual,
    ];

    /// <summary>Catalog-only fields (everything beyond the shared core name/set/cn/type/rarity/color/…).
    /// Marked <see cref="SearchFieldKind.GameSpecific"/> so the collection query routes them through
    /// <c>ResolveFieldCardIds</c>.</summary>
    public static IReadOnlyList<SearchFieldDefinition> GameFields { get; } =
    [
        F("oracle", ["oracle", "o"], "Rules text (use ~ for the card's own name).", "o:\"draw a card\""),
        F("fulloracle", ["fulloracle", "fo"], "Rules text including reminder text.", "fo:trample"),
        F("keyword", ["keyword", "kw"], "Keyword ability.", "kw:flying"),
        F("mana", ["mana", "m", "manacost"], "Mana cost symbols.", "m:{2}{W}{W}"),
        N("cmc", ["cmc", "mv", "manavalue"], "Mana value (supports <, >, <=, >=).", "cmc>=7"),
        N("power", ["power", "pow"], "Creature power (supports <, >, <=, >=; also pow>tou).", "pow>=5"),
        N("toughness", ["toughness", "tou"], "Creature toughness.", "tou<3"),
        N("loyalty", ["loyalty", "loy"], "Planeswalker starting loyalty.", "loy>=5"),
        N("defense", ["defense", "def"], "Battle defense.", "def>=4"),
        F("pt", ["pt", "powtou"], "Combined power/toughness.", "pt:2/2"),
        F("produces", ["produces"], "Mana colours the card can produce.", "produces:g"),
        N("devotion", ["devotion"], "Devotion (number of coloured pips).", "devotion>=3"),
        F("artist", ["artist", "a"], "Illustrator.", "a:\"rebecca guay\""),
        F("flavor", ["flavor", "ft"], "Flavor text.", "ft:goblin"),
        F("watermark", ["watermark", "wm"], "Watermark.", "wm:azorius"),
        F("border", ["border"], "Border colour (black, white, silver, borderless).", "border:borderless"),
        F("frame", ["frame"], "Frame year or effect (2015, showcase, extendedart).", "frame:showcase"),
        F("stamp", ["stamp"], "Security stamp (oval, triangle, acorn, …).", "stamp:acorn"),
        F("layout", ["layout"], "Card layout (normal, split, transform, …).", "layout:transform"),
        F("game", ["game"], "Availability (paper, mtgo, arena).", "game:arena"),
        F("lang", ["lang", "language"], "Language (en, ja, de, …).", "lang:ja"),
        N("year", ["year"], "Release year (supports <, >, <=, >=).", "year>=2020"),
        N("usd", ["usd"], "Price in US dollars.", "usd<1"),
        N("eur", ["eur"], "Price in euros.", "eur<1"),
        N("tix", ["tix"], "Price in MTGO tickets.", "tix<5"),
        N("edhrec", ["edhrec"], "EDHREC popularity rank (lower = more popular).", "edhrec<1000"),
        F("format", ["format", "f", "legal"], "Legal (or restricted) in a format.", "f:modern"),
        F("banned", ["banned"], "Banned in a format.", "banned:legacy"),
        F("restricted", ["restricted"], "Restricted in a format.", "restricted:vintage"),
        F("st", ["st", "settype"], "Set type (expansion, masters, commander, …).", "st:masters"),
        F("in", ["in"], "Printed for a game (paper, mtgo, arena).", "in:paper"),
        F("has", ["has"], "Presence flags: has:watermark, has:indicator.", "has:watermark"),
    ];

    /// <summary>Colour fields split the way the catalog stores them (Colors vs ColorIdentity).</summary>
    private static readonly SearchFieldDefinition[] SplitColorFields =
    [
        new() { Canonical = "colors", Aliases = ["colors", "color", "c"], Kind = SearchFieldKind.GameSpecific,
                SupportedOps = AllOps, Description = "Card colours (WUBRG, colorless, multicolor, or a count).", Example = "c:rg" },
        new() { Canonical = "identity", Aliases = ["identity", "id", "ci", "commander"], Kind = SearchFieldKind.GameSpecific,
                SupportedOps = AllOps, Description = "Colour identity (for Commander).", Example = "id<=wu" },
    ];

    /// <summary>Schema for catalog search — splits <c>c</c> (colours) from <c>id</c> (identity). The
    /// split colour fields are appended last so their aliases win over the core merged <c>color</c>.</summary>
    public static SearchSchema Catalog { get; } =
        new(SharedSearchSchema.CoreFields.Concat(GameFields).Concat(SplitColorFields));

    /// <summary>Schema for the popover + owned-collection search — core (merged colour) plus the
    /// catalog-only game fields.</summary>
    public static SearchSchema Public { get; } = SharedSearchSchema.WithGameFields(GameFields);

    private static SearchFieldDefinition F(string canonical, string[] aliases, string desc, string example) =>
        new() { Canonical = canonical, Aliases = aliases, Kind = SearchFieldKind.GameSpecific, Description = desc, Example = example };

    private static SearchFieldDefinition N(string canonical, string[] aliases, string desc, string example) =>
        new() { Canonical = canonical, Aliases = aliases, Kind = SearchFieldKind.GameSpecific, SupportedOps = AllOps, Description = desc, Example = example };
}
