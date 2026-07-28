using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

/// <summary>
/// Resolves a decklist entry to a specific printing using a graduated ladder based on
/// which fields the line provides. Returns null when the entry cannot be resolved.
/// </summary>
public static class DecklistPrintingResolver
{
    public static CardMatch? Resolve(ICardGameService gs, DecklistEntry entry)
    {
        var set = string.IsNullOrWhiteSpace(entry.SetCode) ? null : entry.SetCode.Trim();
        var cn = string.IsNullOrWhiteSpace(entry.CollectorNumber) ? null : entry.CollectorNumber.Trim();

        // Rung 1: exact set + collector — trust the line; no fallback if it misses.
        if (set is not null && cn is not null)
        {
            var hits = gs.SearchCards($"set:{set} cn:{cn}", maxResults: 50);
            return hits.FirstOrDefault(r =>
                string.Equals(r.SetCode, set, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.CollectorNumber, cn, StringComparison.OrdinalIgnoreCase));
        }

        // Rungs 2-4 operate over all printings of the name.
        var printings = gs.GetPrintings(entry.CardName);
        if (printings.Count == 0)
            return null;

        IEnumerable<CardMatch> candidates = printings;
        if (set is not null)
            candidates = candidates.Where(p => string.Equals(p.SetCode, set, StringComparison.OrdinalIgnoreCase));
        else if (cn is not null)
            candidates = candidates.Where(p => string.Equals(p.CollectorNumber, cn, StringComparison.OrdinalIgnoreCase));

        var list = candidates.ToList();
        if (list.Count == 0)
            return null;

        return Cheapest(gs, list);
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
