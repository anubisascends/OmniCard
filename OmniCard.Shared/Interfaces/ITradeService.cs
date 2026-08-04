using OmniCard.Models;

namespace OmniCard.Interfaces;

/// <summary>Read-side queries over applied trades, for the "Link to Trade" picker.</summary>
public interface ITradeService
{
    /// <summary>Every trade, newest first, with a computed count of replacement lots already
    /// linked to it. There's no "open"/"closed" state — a trade can keep receiving replacements
    /// indefinitely, so the count is informational context for the user, not a filter.</summary>
    List<TradeSummary> GetTrades();

    TradeSummary? GetTrade(int id);
}
