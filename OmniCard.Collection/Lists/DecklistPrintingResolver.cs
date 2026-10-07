using OmniCard.Shared.Games;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Matching;

namespace OmniCard.Collection.Lists;

/// <summary>
/// Resolves a decklist entry to a specific printing using a graduated ladder based on
/// which fields the line provides. Returns null when the entry cannot be resolved.
/// </summary>
public static class DecklistPrintingResolver
{
    /// <summary>Resolves <paramref name="entry"/> to a printing in <paramref name="language"/> (null = English):
    /// a catalog that downloads other languages holds a sibling row per language for one set + collector
    /// number, and the deck's line names the printing, not the language. When the printing doesn't exist in
    /// that language the English row is used (and failing that, whatever the catalog has).</summary>
    public static CardMatch? Resolve(ICardGameService gs, DecklistEntry entry, string? language = null)
    {
        var set = string.IsNullOrWhiteSpace(entry.SetCode) ? null : entry.SetCode.Trim();
        var cn = string.IsNullOrWhiteSpace(entry.CollectorNumber) ? null : entry.CollectorNumber.Trim();

        // Rung 1: exact set + collector — trust the line; no fallback if it misses.
        if (set is not null && cn is not null)
        {
            var hits = gs.SearchCards($"set:{set} cn:{cn}", maxResults: 50).Where(r =>
                string.Equals(r.SetCode, set, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.CollectorNumber, cn, StringComparison.OrdinalIgnoreCase));
            return PreferLanguage(hits, language).FirstOrDefault();
        }

        // Rungs 2-4 operate over all printings of the name.
        var printings = GetPrintingsFuzzy(gs, entry.CardName);
        if (printings.Count == 0)
            return null;

        IEnumerable<CardMatch> candidates = printings;
        if (set is not null)
            candidates = candidates.Where(p => string.Equals(p.SetCode, set, StringComparison.OrdinalIgnoreCase));
        else if (cn is not null)
            candidates = candidates.Where(p => string.Equals(p.CollectorNumber, cn, StringComparison.OrdinalIgnoreCase));

        var list = PreferLanguage(candidates, language);
        if (list.Count == 0)
            return null;

        return Cheapest(gs, list);
    }

    /// <summary>The printings in <paramref name="language"/> (null = English); when there are none, the
    /// English ones; when there are none of those either, all of them.</summary>
    public static List<CardMatch> PreferLanguage(IEnumerable<CardMatch> printings, string? language)
    {
        var all = printings.ToList();
        var target = CardLanguages.Normalize(language) ?? CardLanguages.English;
        var inLanguage = all.Where(p => LanguageOf(p) == target).ToList();
        if (inLanguage.Count > 0) return inLanguage;
        var english = all.Where(p => LanguageOf(p) == CardLanguages.English).ToList();
        return english.Count > 0 ? english : all;
    }

    private static string LanguageOf(CardMatch p) => CardLanguages.Normalize(p.Language) ?? CardLanguages.English;

    /// <summary>Looks up printings by name, falling back to swapping ", " for " - " (e.g. Riftbound's
    /// "Vi, Piltover Enforcer" decklist name vs. "Vi - Piltover Enforcer" in the card database) when the
    /// exact name has no printings.</summary>
    public static List<CardMatch> GetPrintingsFuzzy(ICardGameService gs, string cardName)
    {
        var printings = gs.GetPrintings(cardName);
        if (printings.Count > 0)
            return printings;

        var fuzzyName = cardName.Replace(", ", " - ");
        return fuzzyName != cardName ? gs.GetPrintings(fuzzyName) : printings;
    }

    private static CardMatch Cheapest(ICardGameService gs, List<CardMatch> printings)
    {
        var prices = gs.GetCurrentPrices(printings.Select(p => p.GameSpecificId), isFoil: false);
        var priced = printings
            .Where(p => prices.ContainsKey(p.GameSpecificId))
            .OrderBy(p => prices[p.GameSpecificId])
            .ToList();
        return priced.Count > 0 ? priced[0] : printings[0];
    }
}
