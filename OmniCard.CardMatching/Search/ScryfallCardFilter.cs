using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using OmniCard.Shared.Cards;

namespace OmniCard.CardMatching.Search;

/// <summary>
/// Result-shaping directives pulled out of a Scryfall query (they aren't filters):
/// <c>order:</c>/<c>direction:</c> and <c>unique:</c>.
/// </summary>
public sealed record SearchDirectives(string? Order = null, bool Descending = false, string? Unique = null);

/// <summary>
/// Evaluates a parsed <see cref="FilterNode"/> tree against Magic cards, implementing the full
/// <see href="https://scryfall.com/docs/syntax">Scryfall search syntax</see> for the fields the
/// catalog stores.
///
/// <para>Two representations are produced from the same tree:</para>
/// <list type="bullet">
/// <item><see cref="Matches"/> — an <b>exact</b> in-memory predicate. This is the source of truth and
/// handles everything: list-valued colors/keywords/games, numeric power/toughness, prices, legality,
/// and the <c>is:</c>/<c>has:</c> flags.</item>
/// <item><see cref="BuildSqlPrefilter"/> — a <b>sound over-approximation</b> that runs in SQL to narrow
/// the catalog before the in-memory pass. It only emits predicates that translate reliably on both
/// SQLite and SQL Server (plain string-column <c>LIKE</c>, the <c>Cmc</c> double, indexed columns) and
/// only for positive, top-level ANDed clauses, so it can never exclude a real match. Everything else it
/// leaves to <see cref="Matches"/>.</item>
/// </list>
///
/// This split is deliberate: the catalog stores colors/keywords/games/legalities as value-converted
/// JSON strings and power/toughness as text, none of which compare correctly (or at all) in provider
/// SQL, so exact evaluation must happen in memory.
/// </summary>
public static class ScryfallCardFilter
{
    // ---------------------------------------------------------------------
    // Directives (order/unique) — stripped before filtering.
    // ---------------------------------------------------------------------

    private static readonly HashSet<string> DirectiveFields =
        new(StringComparer.OrdinalIgnoreCase) { "order", "direction", "unique", "display", "prefer" };

    /// <summary>Splits a parsed tree into the filter portion and the result directives. Directives are
    /// recognised at the top level of the (implicit) AND, which is where Scryfall users put them.</summary>
    public static (FilterNode? Filter, SearchDirectives Directives) ExtractDirectives(FilterNode? node)
    {
        string? order = null, unique = null; bool desc = false;

        FilterNode? Strip(FilterNode? n)
        {
            switch (n)
            {
                case null:
                    return null;
                case FieldFilter f when DirectiveFields.Contains(f.Field):
                    switch (f.Field.ToLowerInvariant())
                    {
                        case "order": order = f.Value.ToLowerInvariant(); break;
                        case "unique": unique = f.Value.ToLowerInvariant(); break;
                        case "direction":
                            desc = f.Value.StartsWith("desc", StringComparison.OrdinalIgnoreCase); break;
                    }
                    return null; // consumed
                case AndFilter and:
                    var kept = and.Children.Select(Strip).Where(c => c is not null).Cast<FilterNode>().ToList();
                    return kept.Count switch { 0 => null, 1 => kept[0], _ => new AndFilter(kept) };
                default:
                    return n;
            }
        }

        var filter = Strip(node);
        return (filter, new SearchDirectives(order, desc, unique));
    }

    // ---------------------------------------------------------------------
    // Exact in-memory evaluation.
    // ---------------------------------------------------------------------

    public static bool Matches(Card card, FilterNode node) => node switch
    {
        FieldFilter f => f.Negated ? !MatchField(card, f) : MatchField(card, f),
        AndFilter and => and.Children.All(c => Matches(card, c)),
        OrFilter or => or.Children.Any(c => Matches(card, c)),
        NotFilter not => !Matches(card, not.Inner),
        _ => true,
    };

    private static bool MatchField(Card c, FieldFilter f)
    {
        var op = f.Op;
        var v = f.Value;

        switch (f.Field)
        {
            case "name":
                return StrMatch(c.Name, op, v);
            case "set":
                // set: is an exact set-code match on Scryfall; also allow set-name substring for convenience.
                return op is ComparisonOp.Exact
                    ? c.SetCode.Equals(v, StringComparison.OrdinalIgnoreCase)
                    : c.SetCode.Equals(v, StringComparison.OrdinalIgnoreCase) || Contains(c.SetName, v);
            case "block":
                return Contains(c.SetName, v);
            case "st": // set type
                return StrMatch(c.SetType, op, v);
            case "cn":
                return CnMatch(c.CollectorNumber, op, v);
            case "type":
                return StrMatch(c.TypeLine, op, v);
            case "oracle":
            case "fulloracle":
                // Scryfall lets ~ in the query stand in for the card's own name.
                return StrMatch(c.OracleText, op, SubstituteName(v, c.Name));
            case "flavor":
                return StrMatch(c.FlavorText, op, v);
            case "keyword":
                return ListContains(c.Keywords, v);
            case "artist":
                return StrMatch(c.Artist, op, v);
            case "watermark":
                return StrMatch(c.Watermark, op, v);
            case "border":
                return StrMatch(c.BorderColor, op, v);
            case "frame":
                return StrMatch(c.Frame, op, v) || ListContains(c.FrameEffects, v);
            case "stamp":
                return StrMatch(c.SecurityStamp, op, v);
            case "layout":
                return StrMatch(c.Layout, op, v);
            case "lang":
                return c.Lang.Equals(NormalizeLang(v), StringComparison.OrdinalIgnoreCase);
            case "game":
                return ListContains(c.Games, v);
            case "mana":
                return ManaMatch(c.ManaCost, op, v);
            case "cmc":
                return NumMatch(c.Cmc, op, v);
            case "power":
                return StatMatch(c, c.Power, op, v);
            case "toughness":
                return StatMatch(c, c.Toughness, op, v);
            case "loyalty":
                return StatMatch(c, c.Loyalty, op, v);
            case "defense":
                return StatMatch(c, c.Defense, op, v);
            case "pt": // combined "p/t"
                return PtMatch(c, v);
            case "color":
            case "colors":
                return ColorMatch(c.Colors, op, v);
            case "identity":
                return ColorMatch(c.ColorIdentity, op, v);
            case "produces":
                return ColorMatch(c.ProducedMana, op, v);
            case "devotion":
                return NumMatch(Devotion(c), op, v);
            case "rarity":
                return RarityMatch(c.Rarity, op, v);
            case "year":
                return YearMatch(c.ReleasedAt, op, v);
            case "date":
                return DateMatch(c.ReleasedAt, op, v);
            case "usd":
                return PriceMatch(c.Prices?.Usd, op, v);
            case "eur":
                return PriceMatch(c.Prices?.Eur, op, v);
            case "tix":
                return PriceMatch(c.Prices?.Tix, op, v);
            case "edhrec":
                return c.EdhrecRank.HasValue && NumMatch(c.EdhrecRank.Value, op, v);
            case "format":
                return LegalityMatch(c, v, "legal", "restricted");
            case "banned":
                return LegalityMatch(c, v, "banned");
            case "restricted":
                return LegalityMatch(c, v, "restricted");
            case "in":
                return InMatch(c, v);
            case "foil": // legacy foil:true/false
                return IsTrue(v) ? c.Foil : !c.Foil;
            case "has":
                return HasMatch(c, v);
            case "is":
                return IsMatch(c, v);
            case "order" or "direction" or "unique" or "display" or "prefer":
                return true; // directives never filter
            default:
                // Unknown field → treat as a name search (mirrors Scryfall's forgiving fallback).
                return StrMatch(c.Name, op, v);
        }
    }

    // ---------------------------------------------------------------------
    // Field helpers.
    // ---------------------------------------------------------------------

    private static bool Contains(string? s, string v) =>
        s is not null && s.Contains(v, StringComparison.OrdinalIgnoreCase);

    private static bool StrMatch(string? s, ComparisonOp op, string v) => op switch
    {
        ComparisonOp.Exact => s is not null && s.Equals(v, StringComparison.OrdinalIgnoreCase),
        ComparisonOp.NotEqual => s is null || !s.Contains(v, StringComparison.OrdinalIgnoreCase),
        _ => Contains(s, v),
    };

    private static bool ListContains(IReadOnlyList<string>? list, string v) =>
        list is not null && list.Any(x => x.Equals(v, StringComparison.OrdinalIgnoreCase));

    private static bool CnMatch(string cn, ComparisonOp op, string v)
    {
        // Prefer numeric comparison when both sides are integers (collector numbers can be "123a").
        if (int.TryParse(cn, out var cnNum) && int.TryParse(v, out var vNum))
            return CompareNum(cnNum, op, vNum);
        return op == ComparisonOp.NotEqual
            ? !cn.Equals(v, StringComparison.OrdinalIgnoreCase)
            : cn.Equals(v, StringComparison.OrdinalIgnoreCase);
    }

    private static string SubstituteName(string value, string name)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains('~')) return value;
        var shortName = name.Split(" // ")[0];
        return value.Replace("~", shortName, StringComparison.Ordinal);
    }

    private static string NormalizeLang(string v) => v.ToLowerInvariant() switch
    {
        "english" => "en", "spanish" => "es", "french" => "fr", "german" => "de",
        "italian" => "it", "portuguese" => "pt", "japanese" => "ja", "korean" => "ko",
        "russian" => "ru", "chinese" or "schinese" => "zhs", "tchinese" => "zht",
        _ => v,
    };

    private static bool NumMatch(double actual, ComparisonOp op, string v) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && CompareNum(actual, op, n);

    private static bool CompareNum(double a, ComparisonOp op, double b) => op switch
    {
        ComparisonOp.LessThan => a < b,
        ComparisonOp.GreaterThan => a > b,
        ComparisonOp.LessOrEqual => a <= b,
        ComparisonOp.GreaterOrEqual => a >= b,
        ComparisonOp.NotEqual => Math.Abs(a - b) > double.Epsilon,
        _ => Math.Abs(a - b) < double.Epsilon,
    };

    /// <summary>Power/toughness/loyalty/defense. Supports numeric compares, star/X literals, and the
    /// cross-stat form (<c>pow&gt;tou</c>).</summary>
    private static bool StatMatch(Card c, string? stat, ComparisonOp op, string v)
    {
        if (stat is null) return false;

        // Cross-field: pow>tou, tou<=pow, etc.
        var other = CrossStat(c, v);
        if (other is not null && TryStatValue(stat, out var lhs))
            return CompareNum(lhs, op, other.Value);

        // Literal star / X comparison (exact/contains/notequal only).
        if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var target))
            return op == ComparisonOp.NotEqual
                ? !stat.Equals(v, StringComparison.OrdinalIgnoreCase)
                : stat.Equals(v, StringComparison.OrdinalIgnoreCase);

        return TryStatValue(stat, out var actual) && CompareNum(actual, op, target);
    }

    private static double? CrossStat(Card c, string v) => v.ToLowerInvariant() switch
    {
        "pow" or "power" => TryStatValue(c.Power, out var p) ? p : null,
        "tou" or "toughness" => TryStatValue(c.Toughness, out var t) ? t : null,
        "loy" or "loyalty" => TryStatValue(c.Loyalty, out var l) ? l : null,
        "cmc" or "mv" => c.Cmc,
        _ => null,
    };

    private static bool TryStatValue(string? stat, out double value)
    {
        // Treat "*" and "1+*" etc. as 0 for ordering, matching Scryfall's convention.
        if (stat is not null && stat.Contains('*')) { value = 0; return true; }
        return double.TryParse(stat, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool PtMatch(Card c, string v)
    {
        var parts = v.Split('/');
        if (parts.Length != 2) return false;
        return StatMatch(c, c.Power, ComparisonOp.Exact, parts[0])
            && StatMatch(c, c.Toughness, ComparisonOp.Exact, parts[1]);
    }

    private static bool ManaMatch(string? manaCost, ComparisonOp op, string v)
    {
        if (manaCost is null) return false;
        var have = ManaSymbols(manaCost);
        var want = ManaSymbols(v);
        // : / >= — cost contains at least the requested symbols (by count).
        // = — exactly these symbols.
        return op switch
        {
            ComparisonOp.Exact => SameSymbols(have, want),
            ComparisonOp.NotEqual => !SameSymbols(have, want),
            _ => want.All(kv => have.TryGetValue(kv.Key, out var n) && n >= kv.Value),
        };
    }

    private static Dictionary<string, int> ManaSymbols(string cost)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Braced symbols first: {W}{2/U}{G} …
        foreach (var token in System.Text.RegularExpressions.Regex.Matches(cost, "{([^}]+)}")
                     .Select(m => m.Groups[1].Value))
            Bump(result, token);

        // Bare form like "2WW" or "wwu".
        if (!cost.Contains('{'))
        {
            int i = 0;
            while (i < cost.Length)
            {
                char ch = cost[i];
                if (char.IsDigit(ch))
                {
                    int j = i; while (j < cost.Length && char.IsDigit(cost[j])) j++;
                    Bump(result, cost[i..j]); i = j;
                }
                else if (!char.IsWhiteSpace(ch)) { Bump(result, ch.ToString()); i++; }
                else i++;
            }
        }
        return result;

        static void Bump(Dictionary<string, int> d, string sym)
        {
            sym = sym.ToUpperInvariant();
            if (int.TryParse(sym, out var generic)) { d["generic"] = d.GetValueOrDefault("generic") + generic; }
            else d[sym] = d.GetValueOrDefault(sym) + 1;
        }
    }

    private static bool SameSymbols(Dictionary<string, int> a, Dictionary<string, int> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var n) && n == kv.Value);

    private static int Devotion(Card c)
    {
        if (c.ManaCost is null) return 0;
        var syms = ManaSymbols(c.ManaCost);
        return syms.Where(kv => kv.Key != "generic").Sum(kv => kv.Value);
    }

    /// <summary>Colors / color identity / produced mana. Value is WUBRG letters, a colour word,
    /// <c>colorless</c>/<c>c</c>, <c>multicolor</c>/<c>m</c>, or a number (colour count).</summary>
    private static bool ColorMatch(IReadOnlyList<string>? cardColors, ComparisonOp op, string v)
    {
        var have = new HashSet<char>((cardColors ?? []).SelectMany(s => s.ToUpperInvariant()).Where("WUBRG".Contains));

        var lower = v.ToLowerInvariant();
        if (lower is "colorless" or "c")
            return op == ComparisonOp.NotEqual ? have.Count != 0 : have.Count == 0;
        if (lower is "multicolor" or "multicolored" or "m")
            return op == ComparisonOp.NotEqual ? have.Count < 2 : have.Count >= 2;

        // Numeric colour-count comparison, e.g. c>=2.
        if (int.TryParse(v, out var count))
            return CompareNum(have.Count, op, count);

        var want = new HashSet<char>(ScryfallQueryParser.NormalizeColorValue(v));
        return op switch
        {
            ComparisonOp.Exact => have.SetEquals(want),
            ComparisonOp.NotEqual => !have.SetEquals(want),
            ComparisonOp.LessOrEqual => have.IsSubsetOf(want),
            ComparisonOp.LessThan => have.IsProperSubsetOf(want),
            ComparisonOp.GreaterThan => have.IsProperSupersetOf(want),
            _ => want.IsSubsetOf(have), // : and >=
        };
    }

    private static bool RarityMatch(string rarity, ComparisonOp op, string v)
    {
        if (op is ComparisonOp.Contains or ComparisonOp.Exact)
            return rarity.Equals(v, StringComparison.OrdinalIgnoreCase);
        return ScryfallQueryParser.RaritiesMatching(op, v)
            .Any(r => r.Equals(rarity, StringComparison.OrdinalIgnoreCase));
    }

    private static bool YearMatch(string releasedAt, ComparisonOp op, string v)
    {
        if (releasedAt.Length < 4 || !int.TryParse(releasedAt[..4], out var year)) return false;
        return NumMatch(year, op, v);
    }

    private static bool DateMatch(string releasedAt, ComparisonOp op, string v)
    {
        if (!DateTime.TryParse(releasedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var released)) return false;
        if (!DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var target)) return false;
        return CompareNum(released.Ticks, op, target.Ticks);
    }

    private static bool PriceMatch(string? price, ComparisonOp op, string v)
    {
        if (price is null || !decimal.TryParse(price, NumberStyles.Number, CultureInfo.InvariantCulture, out var actual)) return false;
        if (!decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var target)) return false;
        return CompareNum((double)actual, op, (double)target);
    }

    private static bool LegalityMatch(Card c, string format, params string[] statuses)
    {
        if (!c.Legalities.TryGetValue(format.ToLowerInvariant(), out var status)) return false;
        return statuses.Any(s => status.Equals(s, StringComparison.OrdinalIgnoreCase));
    }

    private static bool InMatch(Card c, string v) => v.ToLowerInvariant() switch
    {
        "paper" or "mtgo" or "arena" => ListContains(c.Games, v),
        _ => ListContains(c.Games, v),
    };

    private static bool IsTrue(string v) =>
        v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v == "1";

    private static bool HasMatch(Card c, string v) => v.ToLowerInvariant() switch
    {
        "watermark" => !string.IsNullOrEmpty(c.Watermark),
        "indicator" or "colorindicator" => c.ColorIndicator is { Count: > 0 },
        "flavor" => !string.IsNullOrEmpty(c.FlavorText),
        _ => false,
    };

    private static readonly string[] PermanentTypes = ["artifact", "creature", "enchantment", "land", "planeswalker", "battle"];

    private static bool IsMatch(Card c, string v)
    {
        var val = v.ToLowerInvariant();
        var type = c.TypeLine.ToLowerInvariant();
        return val switch
        {
            "foil" => c.Foil,
            "nonfoil" => c.Nonfoil,
            "etched" => ListContains(c.Finishes, "etched"),
            "glossy" => ListContains(c.Finishes, "glossy"),
            "promo" => c.Promo,
            "reprint" => c.Reprint,
            "firstprint" or "firstprinting" => !c.Reprint,
            "reserved" => c.Reserved,
            "digital" => c.Digital,
            "fullart" or "full" => c.FullArt,
            "textless" => c.Textless,
            "booster" => c.Booster,
            "oversized" => c.Oversized,
            "spotlight" or "storyspotlight" => c.StorySpotlight,
            "variation" => c.Variation,
            "gamechanger" => c.GameChanger,
            "hires" or "highres" => c.HighresImage,
            "contentwarning" => c.ContentWarning == true,
            "colorless" => ColorMatch(c.Colors, ComparisonOp.Exact, "colorless"),
            "multicolor" or "multicolored" or "gold" => ColorMatch(c.Colors, ComparisonOp.Contains, "multicolor"),
            "hybrid" => c.ManaCost is not null && c.ManaCost.Contains('/') && !c.ManaCost.Contains("/P"),
            "phyrexian" => c.ManaCost is not null && c.ManaCost.Contains("/P", StringComparison.OrdinalIgnoreCase),
            "split" => c.Layout == "split",
            "flip" => c.Layout == "flip",
            "transform" => c.Layout == "transform",
            "meld" => c.Layout == "meld",
            "leveler" => c.Layout == "leveler",
            "dfc" or "doublefaced" => c.Layout is "transform" or "modal_dfc" or "double_faced_token",
            "mdfc" or "modaldfc" => c.Layout == "modal_dfc",
            "adventure" => c.Layout == "adventure",
            "token" => c.Layout is "token" or "double_faced_token" || type.Contains("token"),
            "permanent" => PermanentTypes.Any(type.Contains) && !type.Contains("token"),
            "spell" => !type.Contains("land") && !type.Contains("token"),
            "land" => type.Contains("land"),
            "creature" => type.Contains("creature"),
            "vanilla" => type.Contains("creature") && string.IsNullOrEmpty(c.OracleText),
            "commander" => type.Contains("legendary") && type.Contains("creature")
                           || (c.OracleText?.Contains("can be your commander", StringComparison.OrdinalIgnoreCase) ?? false),
            "funny" => c.BorderColor == "silver" || c.SetType is "funny" or "memorabilia",
            _ => false,
        };
    }

    // ---------------------------------------------------------------------
    // Result shaping (unique / order) — applied after filtering.
    // ---------------------------------------------------------------------

    /// <summary>Applies <c>unique:</c> deduplication. Default (null / "prints") keeps every printing.</summary>
    public static IEnumerable<Card> ApplyUnique(IEnumerable<Card> cards, string? unique) => unique switch
    {
        "cards" => cards.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()),
        "art" => cards.GroupBy(c => c.IllustrationId ?? c.Id).Select(g => g.First()),
        _ => cards,
    };

    /// <summary>Applies <c>order:</c>/<c>direction:</c>. Falls back to name-ascending.</summary>
    public static IEnumerable<Card> ApplyOrder(IEnumerable<Card> cards, SearchDirectives d)
    {
        Func<Card, IComparable> key = d.Order switch
        {
            "cmc" or "mv" or "manavalue" => c => c.Cmc,
            "power" or "pow" => c => StatKey(c.Power),
            "toughness" or "tou" => c => StatKey(c.Toughness),
            "loyalty" or "loy" => c => StatKey(c.Loyalty),
            "released" or "date" => c => c.ReleasedAt,
            "rarity" => c => ScryfallQueryParser.RarityRank(c.Rarity),
            "color" => c => (c.Colors ?? []).Count,
            "usd" => c => PriceKey(c.Prices?.Usd),
            "eur" => c => PriceKey(c.Prices?.Eur),
            "tix" => c => PriceKey(c.Prices?.Tix),
            "edhrec" => c => c.EdhrecRank ?? int.MaxValue,
            "set" => c => c.SetCode,
            "artist" => c => c.Artist ?? "",
            "cn" or "collector" or "number" => c => int.TryParse(c.CollectorNumber, out var n) ? n : int.MaxValue,
            _ => c => c.Name,
        };
        var ordered = cards.OrderBy(key);
        // Stable secondary sort by name keeps results deterministic.
        var withName = ordered.ThenBy(c => c.Name);
        return d.Descending ? withName.Reverse() : withName;

        static double StatKey(string? s) => TryStatValue(s, out var v) ? v : double.MaxValue;
        static double PriceKey(string? s) => decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? (double)v : double.MaxValue;
    }

    // ---------------------------------------------------------------------
    // Sound SQL prefilter (over-approximation).
    // ---------------------------------------------------------------------

    private static readonly System.Reflection.MethodInfo LikeMethod =
        typeof(DbFunctionsExtensions).GetMethod(nameof(DbFunctionsExtensions.Like),
            [typeof(DbFunctions), typeof(string), typeof(string)])!;

    /// <summary>Builds a predicate that is guaranteed to be true for every card the exact matcher
    /// accepts (a superset), using only reliably-translatable operations. Returns null when nothing can
    /// be safely narrowed (then the caller streams the whole catalog through <see cref="Matches"/>).
    /// Only positive, top-level ANDed field clauses on safe columns contribute.</summary>
    public static Expression<Func<Card, bool>>? BuildSqlPrefilter(FilterNode node)
    {
        var p = Expression.Parameter(typeof(Card), "c");
        var clauses = new List<Expression>();
        Collect(node, clauses, p);
        if (clauses.Count == 0) return null;
        var body = clauses.Aggregate(Expression.AndAlso);
        return Expression.Lambda<Func<Card, bool>>(body, p);
    }

    private static void Collect(FilterNode node, List<Expression> clauses, ParameterExpression p)
    {
        switch (node)
        {
            case AndFilter and:
                foreach (var child in and.Children) Collect(child, clauses, p);
                break;
            case FieldFilter { Negated: false } f:
                var e = SafeClause(p, f);
                if (e is not null) clauses.Add(e);
                break;
            // OrFilter / NotFilter / negated fields: skip — let the exact matcher decide.
        }
    }

    private static Expression? SafeClause(ParameterExpression p, FieldFilter f)
    {
        Expression Like(string prop, string pattern) => Expression.AndAlso(
            Expression.NotEqual(Expression.Property(p, prop), Expression.Constant(null, typeof(string))),
            Expression.Call(LikeMethod, Expression.Property(null, typeof(EF), nameof(EF.Functions)),
                Expression.Property(p, prop), Expression.Constant(pattern)));

        Expression Substr(string prop) => Like(prop, $"%{f.Value}%");

        switch (f.Field)
        {
            case "name": return f.Op == ComparisonOp.Exact ? Like("Name", f.Value)
                                : f.Op == ComparisonOp.NotEqual ? null : Substr("Name");
            case "type": return f.Op == ComparisonOp.NotEqual ? null : Substr("TypeLine");
            case "oracle" or "fulloracle": return f.Op == ComparisonOp.NotEqual ? null : Substr("OracleText");
            case "flavor": return f.Op == ComparisonOp.NotEqual ? null : Substr("FlavorText");
            case "artist": return f.Op == ComparisonOp.NotEqual ? null : Substr("Artist");
            case "watermark": return f.Op == ComparisonOp.NotEqual ? null : Substr("Watermark");
            case "cn": return f.Op is ComparisonOp.Contains or ComparisonOp.Exact
                            ? Expression.Equal(Expression.Property(p, "CollectorNumber"), Expression.Constant(f.Value))
                            : null;
            case "cmc":
                if (!double.TryParse(f.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mv)) return null;
                var cmc = Expression.Property(p, "Cmc");
                var k = Expression.Constant(mv);
                return f.Op switch
                {
                    ComparisonOp.LessThan => Expression.LessThan(cmc, k),
                    ComparisonOp.GreaterThan => Expression.GreaterThan(cmc, k),
                    ComparisonOp.LessOrEqual => Expression.LessThanOrEqual(cmc, k),
                    ComparisonOp.GreaterOrEqual => Expression.GreaterThanOrEqual(cmc, k),
                    ComparisonOp.NotEqual => null,
                    _ => Expression.Equal(cmc, k),
                };
            default:
                return null; // everything else handled exactly in memory
        }
    }
}
