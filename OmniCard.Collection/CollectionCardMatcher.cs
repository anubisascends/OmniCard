using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Scanning;
using OmniCard.CardMatching.Search;

namespace OmniCard.Collection;

/// <summary>
/// In-memory counterpart to <see cref="CollectionQueryBuilder"/>'s Scryfall-syntax filter. The
/// query builder compiles the filter into an EF/SQL expression tree (it uses <c>EF.Functions.Like</c>
/// and can only run against the database); this evaluates the same parsed <see cref="FilterNode"/>
/// tree against already-materialized <see cref="CollectionCard"/> objects, so surfaces holding cards
/// that aren't DB rows — e.g. the binder import-audit tray — can offer the identical
/// <c>c:u</c> / <c>r&gt;=rare</c> / <c>set:dom</c> / <c>t:creature</c> syntax.
///
/// Field semantics are kept in lockstep with <see cref="CollectionQueryBuilder"/>; the SQL there is
/// the source of truth if the two ever drift.
/// </summary>
public static class CollectionCardMatcher
{
    /// <summary>Filters <paramref name="cards"/> by a Scryfall-syntax <paramref name="query"/>. An
    /// empty/whitespace or unparseable query returns every card.</summary>
    /// <summary>Optional per-(game,field,op,value) resolver of catalog GameCardIds for game-specific
    /// fields (element:, might:, …). Signature matches <see cref="IGameFieldResolver.ResolveFieldCardIds"/>
    /// per game; returns null when the field isn't game-specific for that game.</summary>
    public delegate IReadOnlySet<string>? GameFieldResolve(CardGame game, string field, ComparisonOp op, string value);

    public static List<CollectionCard> Filter(IEnumerable<CollectionCard> cards, string? query,
        IReadOnlyDictionary<CardGame, ICardGameService>? gameServices = null, SearchSchema? schema = null)
    {
        var list = cards as IReadOnlyList<CollectionCard> ?? cards.ToList();
        if (string.IsNullOrWhiteSpace(query))
            return list.ToList();

        var filter = ScryfallQueryParser.ParseFilter(query, schema);
        if (filter is null) return list.ToList();

        var resolve = gameServices is null ? null : CreateResolver(gameServices, list);
        return list.Where(c => Matches(c, filter, resolve)).ToList();
    }

    /// <summary>A memoizing <see cref="GameFieldResolve"/> over <paramref name="gameServices"/> whose
    /// answers are restricted to the printings in <paramref name="cards"/> — the only ones that can
    /// match, so MTG checks just their catalog rows rather than the whole catalog.</summary>
    public static GameFieldResolve CreateResolver(
        IReadOnlyDictionary<CardGame, ICardGameService> gameServices, IEnumerable<CollectionCard> cards)
    {
        var candidates = cards.GroupBy(c => c.Game).ToDictionary(
            g => g.Key,
            g => (IReadOnlyCollection<string>)g.Select(c => c.GameCardId).Where(id => id.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        // Memoize id-set resolution so a game-specific field isn't re-resolved per card.
        var cache = new Dictionary<(CardGame, string, ComparisonOp, string), IReadOnlySet<string>?>();
        return (game, field, op, value) =>
        {
            var key = (game, field, op, value);
            if (!cache.TryGetValue(key, out var set))
            {
                set = gameServices.TryGetValue(game, out var svc) && svc is IGameFieldResolver r
                    ? r.ResolveFieldCardIds(field, op, value, candidates.GetValueOrDefault(game) ?? [])
                    : null;
                cache[key] = set;
            }
            return set;
        };
    }

    public static bool Matches(CollectionCard card, FilterNode node, GameFieldResolve? resolve = null) => node switch
    {
        FieldFilter f => MatchField(card, f, resolve),
        AndFilter and => and.Children.All(c => Matches(card, c, resolve)),
        OrFilter or => or.Children.Any(c => Matches(card, c, resolve)),
        NotFilter not => !Matches(card, not.Inner, resolve),
        _ => true,
    };

    private static bool MatchField(CollectionCard c, FieldFilter f, GameFieldResolve? resolve)
    {
        var result = f.Field switch
        {
            "name" => StrOp(c.Name, f.Op, f.Value),
            "set" => f.Op == ComparisonOp.NotEqual ? !Eq(c.SetCode, f.Value) : Eq(c.SetCode, f.Value),
            "cn" => f.Op == ComparisonOp.NotEqual ? c.Number != f.Value : c.Number == f.Value,
            "type" => NullableStrOp(c.CardType, f.Op, f.Value),
            "rarity" => RarityMatch(c.Rarity, f.Op, f.Value),
            "color" => ColorMatch(c.Color, f.Op, f.Value),
            // Ownership flags from the lot; any other is: is a catalog flag (is:commander, …), and one
            // no game recognizes matches nothing — mirrors CollectionQueryBuilder.
            "is" => IsMatch(c, f.Value) ?? GameFieldMatch(c, f, resolve) ?? false,
            "foil" => c.IsFoil == ParseFoil(f.Value),
            "condition" or "cond" => StrOp(c.Condition, f.Op, f.Value),
            "lang" or "language" => LanguageMatch(c.Language, f.Op, f.Value),
            "location" or "loc" => LocationMatch(c.Container?.Name, f.Op, f.Value),
            "tag" => TagMatch(c.Tags, f.Op, f.Value),
            // Unknown field: try the card's game resolver (element:, might:, …); else name search.
            _ => GameFieldMatch(c, f, resolve) ?? StrOp(c.Name, f.Op, f.Value),
        };

        return f.Negated ? !result : result;
    }

    private static bool LanguageMatch(string language, ComparisonOp op, string value)
    {
        var code = CardLanguages.Normalize(value) ?? value.Trim().ToLowerInvariant();
        var equal = string.Equals(language, code, StringComparison.OrdinalIgnoreCase);
        return op == ComparisonOp.NotEqual ? !equal : equal;
    }

    private static bool? GameFieldMatch(CollectionCard c, FieldFilter f, GameFieldResolve? resolve)
    {
        var ids = resolve?.Invoke(c.Game, f.Field, f.Op, f.Value);
        return ids is null ? null : ids.Contains(c.GameCardId);
    }

    // name / condition — matches CollectionQueryBuilder.BuildStringExpression/BuildNameExpression.
    private static bool StrOp(string? value, ComparisonOp op, string term) => op switch
    {
        ComparisonOp.Exact => Eq(value, term),
        ComparisonOp.NotEqual => !Eq(value, term),
        _ => Contains(value, term),
    };

    // type — matches BuildNullableStringExpression (null-safe).
    private static bool NullableStrOp(string? value, ComparisonOp op, string term) => op switch
    {
        ComparisonOp.NotEqual => value is null || !Eq(value, term),
        ComparisonOp.Exact => value is not null && Eq(value, term),
        _ => value is not null && Contains(value, term),
    };

    private static bool RarityMatch(string? rarity, ComparisonOp op, string value)
    {
        if (op is ComparisonOp.Contains or ComparisonOp.Exact)
            return Eq(rarity, value);

        var matching = ScryfallQueryParser.RaritiesMatching(op, value);
        return matching.Any(r => Eq(rarity, r));
    }

    private static bool? IsMatch(CollectionCard c, string value) => value.ToLowerInvariant() switch
    {
        "foil" => c.IsFoil,
        "nonfoil" => !c.IsFoil,
        "missing" => c.IsMissing,
        "missingdb" => c.FlagReason == FlagReason.MissingFromDatabase,
        _ => null,
    };

    private static bool ParseFoil(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        value == "1";

    private static bool LocationMatch(string? name, ComparisonOp op, string value) => op switch
    {
        ComparisonOp.NotEqual => name is null || !Eq(name, value),
        ComparisonOp.Exact => name is not null && Eq(name, value),
        _ => name is not null && Contains(name, value),
    };

    private static bool TagMatch(IEnumerable<string> tags, ComparisonOp op, string value) => op == ComparisonOp.Exact
        ? tags.Any(t => Eq(t, value))
        : tags.Any(t => Contains(t, value));

    // color — mirrors CollectionQueryBuilder.BuildColorExpression.
    private static bool ColorMatch(string? color, ComparisonOp op, string value)
    {
        var colorlessBucket = string.IsNullOrEmpty(color) || Contains(color, "Colorless") || Contains(color, "Land");

        if (Eq(value, "colorless") || Eq(value, "c"))
            return op == ComparisonOp.NotEqual ? !colorlessBucket : colorlessBucket;

        if (Eq(value, "multicolor") || Eq(value, "multi"))
        {
            var isMulti = !string.IsNullOrEmpty(color) && !colorlessBucket && color!.Length >= 2;
            return op == ComparisonOp.NotEqual ? !isMulti : isMulti;
        }

        var normalized = ScryfallQueryParser.NormalizeColorValue(value);
        if (normalized.Length == 0)
            return true;

        var notColorlessBucket = !string.IsNullOrEmpty(color) && !colorlessBucket;

        bool Superset() => notColorlessBucket && normalized.All(ch => color!.Contains(ch, StringComparison.OrdinalIgnoreCase));
        bool Subset()
        {
            if (!notColorlessBucket) return false;
            foreach (var ch in "WUBRG")
                if (!normalized.Contains(ch) && color!.Contains(ch, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }
        bool ExactColors() => notColorlessBucket && Eq(color, normalized);

        return op switch
        {
            ComparisonOp.Contains or ComparisonOp.GreaterOrEqual => Superset(),
            ComparisonOp.Exact => ExactColors(),
            ComparisonOp.NotEqual => string.IsNullOrEmpty(color) || colorlessBucket || !Eq(color, normalized),
            ComparisonOp.LessOrEqual => Subset(),
            ComparisonOp.LessThan => Subset() && !ExactColors(),
            ComparisonOp.GreaterThan => Superset() && !ExactColors(),
            _ => Superset(),
        };
    }

    private static bool Eq(string? a, string? b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string? text, string term) => (text ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);
}
