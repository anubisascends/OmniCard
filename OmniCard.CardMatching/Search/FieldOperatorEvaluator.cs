using System.Globalization;

namespace OmniCard.CardMatching.Search;

/// <summary>
/// Shared in-memory evaluation of a single field predicate against a candidate string value. Used by
/// the TCGCSV blob-search path and by <c>CollectionCardMatcher</c>'s game-field path so both apply
/// identical operator semantics. Numeric operators (&lt;, &gt;, &lt;=, &gt;=) compare numerically when
/// both sides parse as numbers, else fall back to case-insensitive string comparison.
/// </summary>
public static class FieldOperatorEvaluator
{
    /// <summary>Does <paramref name="candidate"/> (a card's actual value for the field) satisfy
    /// <paramref name="op"/>/<paramref name="term"/>? A null/absent candidate never matches (positive
    /// semantics — "include missing" is handled by negation upstream).</summary>
    public static bool Matches(string? candidate, ComparisonOp op, string term)
    {
        if (candidate is null) return false;

        switch (op)
        {
            case ComparisonOp.Contains:
                return candidate.Contains(term, StringComparison.OrdinalIgnoreCase);
            case ComparisonOp.Exact:
                return string.Equals(candidate, term, StringComparison.OrdinalIgnoreCase);
            case ComparisonOp.NotEqual:
                return !string.Equals(candidate, term, StringComparison.OrdinalIgnoreCase);
            default:
                return CompareOrdered(candidate, op, term);
        }
    }

    private static bool CompareOrdered(string candidate, ComparisonOp op, string term)
    {
        int cmp;
        if (TryParseNumber(candidate, out var a) && TryParseNumber(term, out var b))
            cmp = a.CompareTo(b);
        else
            cmp = string.Compare(candidate, term, StringComparison.OrdinalIgnoreCase);

        return op switch
        {
            ComparisonOp.LessThan => cmp < 0,
            ComparisonOp.GreaterThan => cmp > 0,
            ComparisonOp.LessOrEqual => cmp <= 0,
            ComparisonOp.GreaterOrEqual => cmp >= 0,
            _ => false,
        };
    }

    /// <summary>Parses a leading number out of a value (e.g. "1-001H" → no; "7" → 7; "1500" → 1500).
    /// Extracts the leading numeric run so values like "3+" still compare.</summary>
    public static bool TryParseNumber(string value, out double number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var span = value.AsSpan().Trim();
        int i = 0;
        if (i < span.Length && (span[i] == '+' || span[i] == '-')) i++;
        int digitsStart = i;
        while (i < span.Length && (char.IsDigit(span[i]) || span[i] == '.')) i++;
        if (i == digitsStart) return false;

        return double.TryParse(span[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }
}
