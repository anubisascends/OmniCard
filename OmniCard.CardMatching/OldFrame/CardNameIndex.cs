using System.Globalization;
using System.Text;

namespace OmniCard.CardMatching.OldFrame;

/// <summary>
/// Fuzzy card-name lookup for noisy title OCR. Names are compared as bare lower-case letter keys
/// ("Mons's Goblin Raiders" → "monssgoblinraiders"), so OCR's split/merged words and dropped apostrophes
/// don't matter. An OCR read also drags in junk from the frame ("| Prodigal Sorcerer *&; 8."), so every
/// contiguous run of the read's words is tried and the best-matching run wins.
/// </summary>
public sealed class CardNameIndex
{
    // Runs shorter than this are too easily an accidental match of a junk token against a short name.
    private const int MinKeyLength = 4;
    private const int MaxWindowWords = 7;

    private readonly Dictionary<int, List<(string Key, string Name)>> _byLength = [];

    public CardNameIndex(IEnumerable<string> names)
    {
        var seen = new HashSet<(string, string)>();
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            Add(Key(name), name);
            // Double-faced / split cards print only the front face's name in the title bar.
            var slash = name.IndexOf(" // ", StringComparison.Ordinal);
            if (slash > 0) Add(Key(name[..slash]), name);
        }

        void Add(string key, string name)
        {
            if (key.Length < MinKeyLength || !seen.Add((key, name))) return;
            if (!_byLength.TryGetValue(key.Length, out var list))
                _byLength[key.Length] = list = [];
            list.Add((key, name));
        }
    }

    /// <summary>Lower-case ASCII letters only, diacritics folded ("Lim-Dûl" → "limdul").</summary>
    public static string Key(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var c = char.ToLowerInvariant(ch);
            if (c is >= 'a' and <= 'z') sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Best catalog name across <paramref name="reads"/>, with its similarity (0–1, 1 = exact key
    /// match), or null when nothing plausible was read.</summary>
    /// <remarks>Runs are ranked by how many letters they explain — length minus twice the edit distance — not
    /// by similarity alone, so the whole title beats a word inside it: "Ftaise Dead" → Raise Dead (9 letters,
    /// 1 error ⇒ 8) over an exact "Dead" (⇒ 4), and "Mons s Goblin Raiders" over "Goblin Raiders".</remarks>
    public (string Name, double Similarity)? BestMatch(IEnumerable<string> reads)
    {
        string? bestName = null;
        double bestSim = 0, bestRank = double.MinValue;
        foreach (var read in reads)
        {
            var words = read.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(Key).Where(k => k.Length > 0).ToArray();
            for (int i = 0; i < words.Length; i++)
            {
                var window = new StringBuilder();
                for (int j = i; j < words.Length && j < i + MaxWindowWords; j++)
                {
                    window.Append(words[j]);
                    if (window.Length < MinKeyLength) continue;
                    var key = window.ToString();
                    var (name, sim, len) = Closest(key);
                    if (name is null) continue;
                    // sim = 1 - dist/len  ⇒  len - 2·dist = len·(2·sim - 1)
                    var rank = len * (2 * sim - 1);
                    if (rank > bestRank) { bestRank = rank; bestSim = sim; bestName = name; }
                }
            }
        }
        return bestName is null ? null : (bestName, bestSim);
    }

    // Closest name to one run, with its similarity and the compared length (the longer of the two keys).
    private (string? Name, double Similarity, int Length) Closest(string key)
    {
        string? best = null;
        double bestSim = 0;
        int bestLen = 0;
        int lo = Math.Max(MinKeyLength, (int)Math.Floor(key.Length * 0.75));
        int hi = (int)Math.Ceiling(key.Length * 1.34);
        for (int len = lo; len <= hi; len++)
        {
            if (!_byLength.TryGetValue(len, out var list)) continue;
            int maxLen = Math.Max(len, key.Length);
            // Only distances that would beat the current best are worth finishing.
            int budget = (int)Math.Floor((1 - bestSim) * maxLen);
            foreach (var (candidate, name) in list)
            {
                var d = BoundedLevenshtein(key, candidate, budget);
                if (d > budget) continue;
                var sim = 1.0 - (double)d / maxLen;
                if (sim > bestSim)
                {
                    bestSim = sim; best = name; bestLen = maxLen;
                    budget = (int)Math.Floor((1 - bestSim) * maxLen);
                    if (d == 0) return (best, 1.0, maxLen);
                }
            }
        }
        return (best, bestSim, bestLen);
    }

    /// <summary>Levenshtein distance, or any value &gt; <paramref name="max"/> once it's known to exceed it.</summary>
    public static int BoundedLevenshtein(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            int rowMin = cur[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                if (cur[j] < rowMin) rowMin = cur[j];
            }
            if (rowMin > max) return max + 1;
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>Similarity (0–1) of two strings' letter keys.</summary>
    public static double KeySimilarity(string a, string b)
    {
        var ka = Key(a); var kb = Key(b);
        int maxLen = Math.Max(ka.Length, kb.Length);
        if (maxLen == 0) return 0;
        return 1.0 - (double)BoundedLevenshtein(ka, kb, maxLen) / maxLen;
    }
}
