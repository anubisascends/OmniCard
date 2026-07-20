using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IAnalyticsService
{
    HoldingsValuation GetHoldings();
    RealizedSummary GetRealized();
}
