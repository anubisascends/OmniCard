using System.Text;

namespace OmniCard.Models;

/// <summary>
/// Fuzzy matching of an OCR'd collector-number string against catalog collector numbers, tolerant
/// of the character confusions that plague small holofoil set-code text (O/0, G/6, S/5, I/1, …).
/// Both sides are <see cref="Canonicalize"/>d (confusable characters collapsed, separators dropped)
/// before a Levenshtein distance is taken. Used by Yu-Gi-Oh! matching, where the printed code
/// (e.g. "GRCR-EN060") is the ground-truth discriminator between otherwise-identical reprints.
/// </summary>
public static class CollectorNumberFuzzyMatcher
{
    /// <summary>Collapse OCR-confusable characters to a canonical form and drop separators.
    /// Applied to both the OCR text and the catalog code before comparison, so a mis-read like
    /// "AGOV-FNU63" and the true "AGOV-EN063" land within a small edit distance.</summary>
    public static string Canonicalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var raw in s)
        {
            var ch = char.ToUpperInvariant(raw);
            if (!char.IsLetterOrDigit(ch)) continue; // drop dashes/spaces/noise
            sb.Append(ch switch
            {
                'O' or 'Q' or 'D' or 'U' => '0',
                'I' or 'L' or 'T' => '1',
                'Z' => '2',
                'S' => '5',
                'B' => '8',
                'G' => '6',
                _ => ch,
            });
        }
        return sb.ToString();
    }

    /// <summary>Edit distance between two raw strings after canonicalization.</summary>
    public static int Distance(string a, string b) => Levenshtein(Canonicalize(a), Canonicalize(b));

    /// <summary>Edit distance between two already-canonical strings (hot path — avoids re-canon).</summary>
    public static int RawDistance(string canonA, string canonB) => Levenshtein(canonA, canonB);

    /// <summary>The set prefix printed before the region/number (e.g. "GRCR" of "GRCR-EN060"),
    /// canonicalized. Falls back to the leading letter run when no separator is present.</summary>
    public static string CanonPrefix(string code)
    {
        var upper = code.ToUpperInvariant();
        var dash = upper.IndexOf('-');
        string head;
        if (dash > 0)
        {
            head = upper[..dash];
        }
        else
        {
            var i = 0;
            while (i < upper.Length && char.IsLetter(upper[i])) i++;
            head = upper[..i];
        }
        return Canonicalize(head);
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
