using System.Text;
using System.Text.RegularExpressions;

namespace OmniCard.Shared.Cards;

/// <summary>
/// Search text read as a collector number, so one search box can take a card's name OR the number printed
/// on it. Games write numbers differently — MTG "19" (printed "0019"), Pokémon "019/165", One Piece
/// "OP01-019", Final Fantasy "1-019H", Yu-Gi-Oh! "LOB-EN019", Riftbound "OGN-019" (stored as set OGN, number
/// 19) — so matching is lenient about leading zeros but strict about the number itself:
/// <list type="bullet">
/// <item>Digits only ("0019", "19"): every printing whose number part (the last run of digits before any
/// "/total") is that number — 19 matches "19", "019/165", "OP01-019", "1-019H", "LOB-EN019", but never
/// "119" or "190".</item>
/// <item>Anything else with a digit ("LOB-EN019", "EN019", "OP01-019", "19a", "OGN-019"): the number as
/// written, ignoring case and leading zeros, either whole or as its tail after a separator (with or
/// without one trailing letter, so "2-019" finds Final Fantasy's "2-019R"), and also against
/// "SET-number" for games that keep the set code apart.</item>
/// </list>
/// </summary>
public sealed partial class CollectorNumberQuery
{
    /// <summary>Longest text still read as a number (a name is usually longer, and always has letters).</summary>
    private const int MaxLength = 24;

    private CollectorNumberQuery(string text, int? number, string? setPrefix, int? prefixedNumber)
    {
        Text = text;
        Number = number;
        SetPrefix = setPrefix;
        PrefixedNumber = prefixedNumber;
        _normalized = Normalize(text);
        _normalizedBeforeSlash = Normalize(BeforeSlash(text));
    }

    /// <summary>The text as typed (trimmed).</summary>
    public string Text { get; }

    /// <summary>The number, when the text is digits only.</summary>
    public int? Number { get; }

    /// <summary>"OGN" in "OGN-019": a set code written in front of the number.</summary>
    public string? SetPrefix { get; }

    /// <summary>19 in "OGN-019".</summary>
    public int? PrefixedNumber { get; }

    private readonly string _normalized;
    private readonly string _normalizedBeforeSlash;

    /// <summary>Reads <paramref name="text"/> as a collector number: one word (no spaces or search
    /// syntax), at most <see cref="MaxLength"/> characters, holding at least one digit. A card name that
    /// happens to fit ("Porygon2") is harmless — the caller searches names too.</summary>
    public static bool TryParse(string? text, out CollectorNumberQuery query)
    {
        query = null!;
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t) || t.Length > MaxLength) return false;
        if (!t.Any(char.IsAsciiDigit)) return false;
        if (t.Any(c => char.IsWhiteSpace(c) || c is ':' or '=' or '<' or '>' or '"' or '(' or ')' or '!')) return false;
        // A leading '-' is the query grammar's negation.
        if (t[0] == '-') return false;

        int? number = t.All(char.IsAsciiDigit) && int.TryParse(t, out var n) ? n : null;
        string? prefix = null;
        int? prefixed = null;
        if (number is null && PrefixedNumberRegex().Match(t) is { Success: true } m
            && int.TryParse(m.Groups["num"].Value, out var pn))
        {
            prefix = m.Groups["set"].Value;
            prefixed = pn;
        }
        query = new CollectorNumberQuery(t, number, prefix, prefixed);
        return true;
    }

    /// <summary>
    /// Query terms (in the shared <c>set:</c>/<c>cn:</c> grammar) whose results include every printing
    /// <see cref="Matches"/> accepts; they may return more (e.g. <c>cn:19</c> is a substring match in some
    /// games), so filter what they return with <see cref="Matches"/>.
    /// </summary>
    public IReadOnlyList<string> CandidateTerms()
    {
        var terms = new List<string>();
        void Add(string term)
        {
            if (!terms.Contains(term)) terms.Add(term);
        }
        if (Number is { } n)
        {
            // Exact-match games (MTG) store "19"; substring games find "019/165", "OP01-019", … from it too.
            Add($"cn:\"{n}\"");
            Add($"cn:\"{Text}\"");
        }
        else
        {
            Add($"cn:\"{Text}\"");
            // Games that keep the set apart from the number (Riftbound "OGN-019" is set OGN, number 19).
            if (SetPrefix is not null && PrefixedNumber is { } pn) Add($"set=\"{SetPrefix}\" cn:\"{pn}\"");
        }
        return terms;
    }

    /// <summary>Whether a printing (its set code and collector number) carries this number.</summary>
    public bool Matches(string? setCode, string? collectorNumber)
    {
        if (string.IsNullOrWhiteSpace(collectorNumber)) return false;
        if (Number is { } n) return NumberPart(collectorNumber) == n;

        foreach (var form in Forms(setCode, collectorNumber))
        {
            var f = Normalize(form);
            var fBeforeSlash = Normalize(BeforeSlash(form));
            if (EqualsOrTail(f, _normalized) || EqualsOrTail(fBeforeSlash, _normalized)
                || EqualsOrTail(fBeforeSlash, _normalizedBeforeSlash))
                return true;
        }
        return false;
    }

    private static IEnumerable<string> Forms(string? setCode, string collectorNumber)
    {
        yield return collectorNumber;
        if (!string.IsNullOrWhiteSpace(setCode)) yield return $"{setCode}-{collectorNumber}";
    }

    /// <summary>The card's own number: the last run of digits before any "/total" ("019/165" → 19,
    /// "OP01-019" → 19, "1-019H" → 19, "19a" → 19). Null when there are no digits.</summary>
    public static int? NumberPart(string? collectorNumber)
    {
        if (string.IsNullOrEmpty(collectorNumber)) return null;
        return TrailingNumber(BeforeSlash(collectorNumber));
    }

    private static int? TrailingNumber(string s)
    {
        var m = LastDigitsRegex().Match(s);
        return m.Success && int.TryParse(m.Value, out var n) ? n : null;
    }

    private static string BeforeSlash(string s)
    {
        var slash = s.IndexOf('/');
        return slash < 0 ? s : s[..slash];
    }

    /// <summary>Equal, or <paramref name="typed"/> is the tail of <paramref name="full"/> after a
    /// separator ("EN19" of "LOB-EN19") — not just any suffix ("9" of "19").</summary>
    private static bool EqualsOrTail(string full, string typed)
    {
        if (typed.Length == 0) return false;
        if (IsTail(full, typed)) return true;
        // Typed without the one letter after the number (Final Fantasy's rarity "2-019R", a variant "19a").
        return char.IsAsciiDigit(typed[^1]) && full.Length > 1 && char.IsAsciiLetter(full[^1]) && char.IsAsciiDigit(full[^2])
            && IsTail(full[..^1], typed);

        static bool IsTail(string full, string typed) =>
            full == typed
            || (full.Length > typed.Length && full.EndsWith(typed, StringComparison.Ordinal)
                && !char.IsLetterOrDigit(full[full.Length - typed.Length - 1]));
    }

    /// <summary>Upper-case, with each run of digits written without leading zeros ("op01-019" → "OP1-19").</summary>
    internal static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            if (char.IsAsciiDigit(s[i]))
            {
                var start = i;
                while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                var run = s[start..i].TrimStart('0');
                sb.Append(run.Length == 0 ? "0" : run);
            }
            else
            {
                sb.Append(char.ToUpperInvariant(s[i]));
                i++;
            }
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"\d+(?=\D*$)")]
    private static partial Regex LastDigitsRegex();

    [GeneratedRegex(@"^(?<set>[A-Za-z][A-Za-z0-9]*)-(?<num>\d+)(?:/\d+)?$")]
    private static partial Regex PrefixedNumberRegex();
}
