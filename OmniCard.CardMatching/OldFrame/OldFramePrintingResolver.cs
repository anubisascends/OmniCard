using System.Text.RegularExpressions;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Matching;

namespace OmniCard.CardMatching.OldFrame;

/// <summary>A catalog printing of the identified card, with its image distances to the scan.</summary>
public sealed record PrintingCandidate(Card Card, int PHashDistance, int? ArtDistance)
{
    /// <summary>Combined image distance (pHash + art hash; pHash doubled when there's no art hash).</summary>
    public int Visual => PHashDistance + (ArtDistance ?? PHashDistance);
}

public sealed record RankedPrinting(PrintingCandidate Candidate, double Score, string Reasons);

/// <summary>
/// Picks the printing of an old-frame (1993/1997 frame) MTG card. Same-art reprints — Alpha / Beta /
/// Unlimited / Revised / 4th, Arabian Nights / Chronicles, Mirage / 6th — hash identically, and these
/// frames print no set code or collector number, so pHash alone picks among them arbitrarily (in practice
/// by lowest collector number, which favours starter/box sets). This scores each same-art printing
/// against what the scan physically shows instead: border colour, whether a copyright line is printed
/// and its year, a printed collector number (1998+ old frames), the Revised-era "Illus. ©" mark, flavor
/// text, and — only as a tie-break — how common the printing is. Lower score is better. Pure, so the
/// rules are unit-testable without images or a catalog.
/// </summary>
public static class OldFramePrintingResolver
{
    // Printings within this many combined image-distance bits of the closest count as the same art. Real
    // same-art reprints land within ~0–4 of each other; different art (FEM/ALL variants, a redrawn reprint)
    // lands 10+ away.
    internal const int SameArtVisualMargin = 8;
    // Residual image distance inside the same-art group only nudges; noise between identical art is not
    // evidence of which printing it is.
    private const double VisualWeight = 0.5;

    internal const double BorderMismatchPenalty = 12;
    internal const double CopyrightLineMismatchPenalty = 8;
    internal const double CopyrightYearMismatchPenalty = 5;
    internal const double CollectorMatchBonus = 10;
    internal const double CollectorMismatchPenalty = 4;
    internal const double CollectorOnPreNumberedPrintingPenalty = 6;
    internal const double IllusMarkSeenPenalty = 5;
    internal const double IllusMarkAbsentPenalty = 3;
    internal const double FlavorMismatchWeight = 8;
    // The pHash pick only breaks exact ties: among identical art it's just "lowest collector number", so it
    // must not outweigh even the smallest real signal (a 1-point scarcity tie-break). It does let a fuzzy
    // user correction that steered the pHash match win a genuine tie.
    internal const double CurrentMatchBonus = 0.5;
    // Candidates within this score of the best are "still tied" for the flavor-text decision.
    internal const double FlavorTieMargin = 3;

    // The copyright line ("© 1994 Wizards of the Coast…") first appeared with Fallen Empires.
    private const string FirstCopyrightLineRelease = "1994-11-01";
    // Printed collector numbers ("nnn/ttt") first appeared with Exodus.
    private const string FirstPrintedCollectorRelease = "1998-06-01";

    // Revised and Summer Magic credit "Illus. © Artist"; Alpha/Beta/Unlimited/CE print "Illus. Artist".
    private static readonly HashSet<string> IllusMarkSets = new(StringComparer.OrdinalIgnoreCase) { "3ed", "sum" };
    private static readonly HashSet<string> NoIllusMarkSets = new(StringComparer.OrdinalIgnoreCase) { "lea", "leb", "2ed", "ced", "cei" };

    // Only a tie-break when the physical evidence can't tell printings apart: how likely a random copy of
    // this art is from this printing. Revised vastly out-printed Unlimited; Summer Magic is near-mythical.
    private static readonly Dictionary<string, double> ScarcityPenalty = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lea"] = 2, ["leb"] = 2, ["2ed"] = 1, ["sum"] = 3,
    };

    private static readonly Dictionary<string, double> SetTypePenalty = new(StringComparer.OrdinalIgnoreCase)
    {
        ["starter"] = 3, ["box"] = 3, ["promo"] = 4, ["memorabilia"] = 6, ["funny"] = 10,
        ["token"] = 20, ["alchemy"] = 20, ["treasure_chest"] = 20, ["vanguard"] = 20, ["minigame"] = 20,
    };
    private const double DeprioritizedSetPenalty = 4;

    private static readonly Regex CopyrightWords = new(@"wiz|izard|coast|caust|const|rights|reserv", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearToken = new(@"(?<!\d)(\d{4,5})(?!\d)", RegexOptions.Compiled);
    // "nnn/ttt": a total of 20+ keeps power/toughness ("2/2", "1/1") out.
    private static readonly Regex CollectorFraction = new(@"(?<!\d)(\d{1,3})\s*/\s*(\d{2,3})(?!\d)", RegexOptions.Compiled);
    // MH2-style retro frames print the bare collector number after the copyright: "…the Coast 382".
    private static readonly Regex CollectorAfterCoast = new(@"coast\W{0,4}(\d{1,4})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // The "Illus." credit token as OCR mangles it: Illus, lllts, Tus, Hlfus, ibus…
    private static readonly Regex CreditToken = new(@"^[a-z]{0,3}(lus|lls|lts|tus|fus|bus)$", RegexOptions.Compiled);

    /// <summary>What the bottom band says, interpreted for one artist's printings.</summary>
    internal sealed record BottomLineFacts(
        bool? HasCopyrightLine,
        IReadOnlySet<int> Years,
        int? CollectorNumber,
        bool CollectorHasTotal,
        bool? IllusMark);

    public static IReadOnlyList<RankedPrinting> Rank(
        IReadOnlyList<PrintingCandidate> candidates,
        MtgPrintEvidence evidence,
        string? textBox,
        string? currentCardId,
        IReadOnlySet<string>? deprioritizedSets = null)
    {
        if (candidates.Count == 0) return [];
        int bestVisual = candidates.Min(c => c.Visual);
        var group = candidates.Where(c => c.Visual <= bestVisual + SameArtVisualMargin).ToList();

        // Same art ⇒ same artist; parse the credit line against it.
        var artist = group.Select(c => c.Card.Artist).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
        var facts = ParseBottomLine(evidence.BottomLineReads, artist);

        var ranked = new List<RankedPrinting>(group.Count);
        foreach (var c in group)
        {
            var card = c.Card;
            var reasons = new List<string>();
            double score = VisualWeight * (c.Visual - bestVisual);

            if (evidence.BorderColor is not null && !string.Equals(card.BorderColor, evidence.BorderColor, StringComparison.OrdinalIgnoreCase))
            {
                score += BorderMismatchPenalty;
                reasons.Add($"border {card.BorderColor}≠{evidence.BorderColor}");
            }

            bool printsCopyright = string.CompareOrdinal(card.ReleasedAt, FirstCopyrightLineRelease) >= 0;
            if (facts.HasCopyrightLine is bool seen && seen != printsCopyright)
            {
                score += CopyrightLineMismatchPenalty;
                reasons.Add(seen ? "copyright line on a pre-FEM printing" : "no copyright line");
            }
            if (printsCopyright && facts.Years.Count > 0 && ReleaseYear(card) is int year
                && !facts.Years.Any(y => Math.Abs(y - year) <= 1))
            {
                score += CopyrightYearMismatchPenalty;
                reasons.Add($"© year {string.Join("/", facts.Years)}≠{year}");
            }

            if (facts.CollectorNumber is int printed)
            {
                bool numbered = string.CompareOrdinal(card.ReleasedAt, FirstPrintedCollectorRelease) >= 0;
                if (LeadingNumber(card.CollectorNumber) == printed)
                {
                    score -= CollectorMatchBonus;
                    reasons.Add($"collector {printed}");
                }
                else if (facts.CollectorHasTotal)
                {
                    score += numbered ? CollectorMismatchPenalty : CollectorOnPreNumberedPrintingPenalty;
                    reasons.Add($"collector {printed}≠{card.CollectorNumber}");
                }
            }

            if (facts.IllusMark == true && NoIllusMarkSets.Contains(card.SetCode))
            {
                score += IllusMarkSeenPenalty;
                reasons.Add("Illus. © on a no-© printing");
            }
            else if (facts.IllusMark == false && IllusMarkSets.Contains(card.SetCode))
            {
                score += IllusMarkAbsentPenalty;
                reasons.Add("no Illus. ©");
            }

            if (textBox is not null)
            {
                var flavor = FlavorCoverage(textBox, card.FlavorText);
                score += FlavorMismatchWeight * (1 - flavor);
                reasons.Add($"flavor {flavor:P0}");
            }

            if (SetTypePenalty.TryGetValue(card.SetType, out var typePenalty))
            {
                score += typePenalty;
                reasons.Add(card.SetType);
            }
            if (deprioritizedSets?.Contains(card.SetCode) == true)
                score += DeprioritizedSetPenalty;
            if (ScarcityPenalty.TryGetValue(card.SetCode, out var scarcity))
                score += scarcity;
            if (currentCardId is not null && string.Equals(card.Id.ToString(), currentCardId, StringComparison.OrdinalIgnoreCase))
                score -= CurrentMatchBonus;

            ranked.Add(new RankedPrinting(c, score, string.Join(", ", reasons)));
        }

        return ranked
            .OrderBy(r => r.Score)
            .ThenBy(r => r.Candidate.Card.ReleasedAt, StringComparer.Ordinal)
            .ThenBy(r => LeadingNumber(r.Candidate.Card.CollectorNumber) ?? int.MaxValue)
            .ToList();
    }

    /// <summary>True when the printings still tied at the top carry different flavor text — the only
    /// thing separating e.g. Alliances' a/b variants — so a text-box read would settle it.</summary>
    public static bool NeedsTextBox(IReadOnlyList<RankedPrinting> ranked)
    {
        if (ranked.Count < 2) return false;
        var best = ranked[0].Score;
        return ranked.Where(r => r.Score <= best + FlavorTieMargin)
            .Select(r => NormalizeFlavor(r.Candidate.Card.FlavorText))
            .Distinct()
            .Count() > 1;
    }

    internal static BottomLineFacts ParseBottomLine(IReadOnlyList<string> reads, string? artist)
    {
        if (reads.Count == 0) return new BottomLineFacts(null, new HashSet<int>(), null, false, null);

        bool sawCopyright = reads.Any(r => CopyrightWords.IsMatch(r));
        bool sawCredit = false;
        int markSeen = 0, markAbsent = 0;
        var years = new HashSet<int>();
        int? collector = null;
        bool collectorHasTotal = false;

        var artistFirst = artist is null ? null : CardNameIndex.Key(artist.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
        var artistLast = artist is null ? null : CardNameIndex.Key(artist.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]);

        foreach (var read in reads)
        {
            foreach (Match m in YearToken.Matches(read))
                foreach (var y in PlausibleYears(m.Groups[1].Value))
                    years.Add(y);

            foreach (Match m in CollectorFraction.Matches(read))
            {
                if (int.Parse(m.Groups[2].Value) < 20) continue;
                collector = int.Parse(m.Groups[1].Value);
                collectorHasTotal = true;
            }
            if (collector is null && CollectorAfterCoast.Match(read) is { Success: true } coast)
                collector = int.Parse(coast.Groups[1].Value);

            var tokens = read.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var keys = tokens.Select(CardNameIndex.Key).ToArray();
            if (keys.Any(k => CreditToken.IsMatch(k))) sawCredit = true;
            if (artistLast is { Length: >= 3 } && keys.Any(k => k.Length >= 3 && Similar(k, artistLast)))
                sawCredit = true;

            switch (IllusMarkIn(tokens, keys, artistFirst))
            {
                case true: markSeen++; break;
                case false: markAbsent++; break;
            }
        }

        // Absence of the copyright line only counts once we know the band was read at all (the credit line
        // was recognised); otherwise it's just an unreadable crop.
        bool? hasCopyright = sawCopyright ? true : sawCredit ? false : null;
        // OCR drops a small "©" more often than it invents one, so one sighting is enough to say it's
        // there, but saying it's absent needs two clean reads with the artist right after "Illus.".
        bool? mark = markSeen > 0 ? true : markAbsent >= 2 ? false : null;
        return new BottomLineFacts(hasCopyright, years, collector, collectorHasTotal, mark);
    }

    // Whether the "©" sits between "Illus." and the artist's first name in one read: true/false when the
    // artist's first name was found, null when the read doesn't show the credit clearly enough to say.
    private static bool? IllusMarkIn(string[] tokens, string[] keys, string? artistFirst)
    {
        if (artistFirst is not { Length: >= 3 }) return null;
        for (int i = 0; i < keys.Length; i++)
        {
            var k = keys[i];
            if (k.Length >= 3 && StartsWithName(k, artistFirst))
            {
                if (i > 0 && IsMarkToken(tokens[i - 1], keys[i - 1])) return true;
                if (i > 0 && CreditToken.IsMatch(keys[i - 1])) return false;
                return null;
            }
            // "©" glued onto the name: "OSandfaEveringham", "Cryesper".
            if (k.Length >= 4 && k[0] is 'c' or 'o' && StartsWithName(k[1..], artistFirst))
                return true;
        }
        return null;
    }

    // OCR often runs the first and last names together ("SandfaEveringham"), so the first name may be the
    // whole token or just its start.
    private static bool StartsWithName(string key, string name) =>
        Similar(key, name) || (key.Length > name.Length && Similar(key[..name.Length], name));

    private static bool IsMarkToken(string raw, string key) =>
        raw.Contains('©') || raw.Contains('@') || raw is "(c)" or "(C)" or "0" || key is "c" or "o" or "e";

    private static bool Similar(string key, string target) =>
        1.0 - (double)CardNameIndex.BoundedLevenshtein(key, target, target.Length) / Math.Max(key.Length, target.Length) >= 0.7;

    /// <summary>Years a 4–5 digit OCR token could be: itself, with one digit dropped (a doubled glyph,
    /// "19906"), and with 0↔9 swapped (the pair Tesseract confuses in this font, "1904" for 1994).</summary>
    internal static IEnumerable<int> PlausibleYears(string token)
    {
        var forms = new HashSet<string>();
        if (token.Length == 4) forms.Add(token);
        else for (int i = 0; i < token.Length; i++) forms.Add(token.Remove(i, 1));
        foreach (var f in forms.ToList())
            for (int i = 0; i < f.Length; i++)
                if (f[i] is '0' or '9')
                    forms.Add(f[..i] + (f[i] == '0' ? '9' : '0') + f[(i + 1)..]);
        return forms.Select(int.Parse).Where(y => y is >= 1993 and <= 2035).Distinct();
    }

    private static int? ReleaseYear(Card card) =>
        card.ReleasedAt.Length >= 4 && int.TryParse(card.ReleasedAt[..4], out var y) ? y : null;

    private static int? LeadingNumber(string collectorNumber)
    {
        int i = 0;
        while (i < collectorNumber.Length && char.IsDigit(collectorNumber[i])) i++;
        return i > 0 && int.TryParse(collectorNumber[..i], out var n) ? n : null;
    }

    private static string NormalizeFlavor(string? flavor) => CardNameIndex.Key(flavor ?? "");

    /// <summary>Share of the flavor text's words (3+ letters) found in the OCR'd text box; 0 when the
    /// printing has no flavor text but the read clearly has words.</summary>
    internal static double FlavorCoverage(string textBox, string? flavor)
    {
        var read = Words(textBox).ToHashSet();
        var want = Words(flavor ?? "").ToList();
        if (want.Count == 0) return 0.5; // unflavored printing: neither confirmed nor ruled out
        return (double)want.Count(read.Contains) / want.Count;
    }

    private static IEnumerable<string> Words(string s) =>
        Regex.Split(s.ToLowerInvariant(), "[^a-z]+").Where(w => w.Length >= 3);
}
