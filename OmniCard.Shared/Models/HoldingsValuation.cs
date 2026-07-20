namespace OmniCard.Models;

public record HoldingsValuation(
    int TotalUnits,
    decimal TotalCost,
    decimal TotalMarket,
    IReadOnlyList<ValuationLine> ByGame,
    IReadOnlyList<ValuationLine> ByCategory,
    IReadOnlyList<ValuationLine> ByLocation);
