using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IAnalyticsService
{
    HoldingsValuation GetHoldings();

    /// <summary>Computes realized P&amp;L from Sell movements. When <paramref name="since"/> is
    /// non-null, only Sell movements with <c>Timestamp &gt;= since</c> are included; the paired
    /// Acquire cost for a lot is always included regardless of its own timestamp. Null (default)
    /// means all-time.</summary>
    RealizedSummary GetRealized(DateTime? since = null);
}
