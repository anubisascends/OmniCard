namespace OmniCard.CardMatching.Games;

/// <summary>A catalog card number the OCR passes voted for. <see cref="Score"/> sums the per-pass votes
/// (an exact read counts 3, a one-edit read 1, split between tied numbers); <see cref="ExactVotes"/> is how
/// many passes read it character-for-character (modulo look-alike glyphs).</summary>
public sealed record OptcgNumberCandidate(string CardNumber, double Score, int ExactVotes);

/// <summary>
/// Snaps noisy OCR of the One Piece collector number ("OP14-109") to the catalog's card numbers.
/// Tesseract reads the card font's digits well but mangles the rest: the round "O" of the prefix is
/// dropped or read as "0" ("P14-109", "0P14-109"), "EB03" comes back "EBO3", the dash goes missing, and
/// the rarity glyph after the number tacks on junk ("OP14-1070AO"). So each read is compared in a
/// look-alike-folded alphabet (O/0, I/1, S/5, B/8…) against every catalog number by edit distance to its
/// best-matching window of the read, and the passes vote.
/// </summary>
public sealed class OptcgCollectorNumberResolver
{
    // Short keys ("P-141" → "P141") occur by accident inside longer reads ("P14-115"), so they must match
    // exactly; full set-prefixed keys may be one edit off (the dropped "O" alone costs one).
    private const int ShortKeyLength = 4;
    // A pass whose best match ties more numbers than this carries no information.
    private const int MaxTiedPerPass = 3;

    private static readonly System.Text.RegularExpressions.Regex DoubledZero =
        new("0O|O0", System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly Dictionary<string, List<string>> _numbersByKey = [];
    private readonly Dictionary<string, HashSet<string>> _keysByTrigram = [];

    public OptcgCollectorNumberResolver(IEnumerable<string> cardNumbers)
    {
        foreach (var number in cardNumbers)
        {
            if (string.IsNullOrWhiteSpace(number)) continue;
            var key = Key(number);
            if (key.Length < ShortKeyLength) continue;
            if (!_numbersByKey.TryGetValue(key, out var numbers))
            {
                _numbersByKey[key] = numbers = [];
                for (int i = 0; i + 3 <= key.Length; i++)
                {
                    var gram = key.Substring(i, 3);
                    if (!_keysByTrigram.TryGetValue(gram, out var keys))
                        _keysByTrigram[gram] = keys = [];
                    keys.Add(key);
                }
            }
            if (!numbers.Contains(number, StringComparer.OrdinalIgnoreCase))
                numbers.Add(number);
        }
    }

    /// <summary>Catalog numbers the reads support, best first (empty when no pass resolved).</summary>
    public IReadOnlyList<OptcgNumberCandidate> Resolve(IEnumerable<string?> reads)
    {
        var score = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var exact = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var read in reads)
        {
            var best = BestInRead(read);
            if (best.Count == 0 || best.Count > MaxTiedPerPass) continue;
            foreach (var (number, distance) in best)
            {
                score[number] = score.GetValueOrDefault(number) + (distance == 0 ? 3.0 : 1.0) / best.Count;
                if (distance == 0) exact[number] = exact.GetValueOrDefault(number) + 1;
            }
        }
        return score
            .Select(kv => new OptcgNumberCandidate(kv.Key, kv.Value, exact.GetValueOrDefault(kv.Key)))
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.ExactVotes)
            .ThenBy(c => c.CardNumber, StringComparer.Ordinal)
            .ToList();
    }

    // The numbers one read best supports. Each whitespace token is matched on its own (a neighbouring
    // subtype word must not lend characters), and the longest credible key wins — ranked by
    // key length − 2·distance, so a full "OP14-115" one edit off beats an exact promo "P-141" inside it.
    internal List<(string Number, int Distance)> BestInRead(string? read)
    {
        var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(read)) return [];
        // Tesseract sometimes reads one zero as two glyphs ("EBO04-055", "OP0O9-055"); a real number
        // never prints a letter O beside a zero, so the collapsed read is tried too.
        var tokens = read.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var collapsed = tokens.Select(t => DoubledZero.Replace(t.ToUpperInvariant(), "0")).Where(t => !tokens.Contains(t, StringComparer.OrdinalIgnoreCase));
        foreach (var token in tokens.Concat(collapsed))
        {
            var text = Key(token);
            if (text.Length < ShortKeyLength) continue;
            var keys = new HashSet<string>();
            for (int i = 0; i + 3 <= text.Length; i++)
                if (_keysByTrigram.TryGetValue(text.Substring(i, 3), out var k))
                    keys.UnionWith(k);
            foreach (var key in keys)
            {
                var maxDistance = key.Length <= ShortKeyLength ? 0 : 1;
                var distance = WindowedDistance(key, text);
                if (distance > maxDistance) continue;
                foreach (var number in _numbersByKey[key])
                    if (!found.TryGetValue(number, out var d) || distance < d)
                        found[number] = distance;
            }
        }
        if (found.Count == 0) return [];
        int Rank(KeyValuePair<string, int> kv) => Key(kv.Key).Length - 2 * kv.Value;
        var top = found.Max(Rank);
        return found.Where(kv => Rank(kv) == top).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>Upper-cased alphanumerics with OCR look-alikes folded together. Applied to both the
    /// catalog numbers and the reads, so "EBO3" and "EB03", or "0P14" and "OP14", compare equal.</summary>
    internal static string Key(string s)
    {
        var chars = new List<char>(s.Length);
        foreach (var raw in s)
        {
            var c = char.ToUpperInvariant(raw);
            if (!char.IsAsciiLetterOrDigit(c)) continue;
            chars.Add(c switch
            {
                'O' or 'Q' or 'D' or 'U' => '0',
                'I' or 'L' or 'T' or 'J' => '1',
                'S' => '5',
                'B' => '8',
                'Z' => '2',
                'G' => '6',
                'A' => '4',
                _ => c,
            });
        }
        return new string([.. chars]);
    }

    /// <summary>Edit distance from <paramref name="pattern"/> to its best-matching substring of
    /// <paramref name="text"/> (semi-global alignment: the text's leading/trailing junk is free).</summary>
    internal static int WindowedDistance(string pattern, string text)
    {
        var prev = new int[text.Length + 1]; // row 0: free start anywhere in the text
        var cur = new int[text.Length + 1];
        for (int i = 1; i <= pattern.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= text.Length; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (pattern[i - 1] == text[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev.Min();
    }
}
