using System.Text.RegularExpressions;

namespace OmniCard.Shared.BuyList;

/// <summary>Whether a card is on the Buy List. <c>CollectionQueryBuilder</c> repeats <see cref="Matches"/>
/// in SQL for <c>is:buylist</c> — keep the two in step.</summary>
public static partial class BuyListMatcher
{
    /// <summary>Printing details TCGplayer-sourced catalogs put in the name: a trailing " (Parallel)" or
    /// " - 199/165".</summary>
    [GeneratedRegex(@"(\s+\([^()]*\)|\s+-\s+\S*\d\S*)+$")]
    private static partial Regex PrintingSuffix();

    /// <summary>The card's name without printing details, so "any printing" reaches every printing.</summary>
    public static string BaseName(string name)
    {
        var trimmed = name.Trim();
        var stripped = PrintingSuffix().Replace(trimmed, "");
        return stripped.Length > 0 ? stripped : trimmed;
    }

    public static bool Matches(BuyListEntry entry, string name, string gameCardId, bool isFoil)
    {
        if (entry.Foil is { } foil && foil != isFoil) return false;
        // The printing it was added from always counts: an owned copy's stored name can differ from the
        // catalog's (one face of a double-faced card, say).
        if (string.Equals(entry.GameCardId, gameCardId, StringComparison.OrdinalIgnoreCase)) return true;
        return !entry.PinPrinting && string.Equals(entry.MatchName, BaseName(name), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Orders competing entries: highest priority, then pinned before any-printing, then oldest.
    /// Copies are taken from entries in this order, and the first one labels the card.</summary>
    public static IEnumerable<BuyListEntry> InMatchOrder(IEnumerable<BuyListEntry> entries) =>
        entries.OrderByDescending(e => e.Priority).ThenByDescending(e => e.PinPrinting).ThenBy(e => e.Id);
}
