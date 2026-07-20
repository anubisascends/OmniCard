namespace OmniCard.Models;

public record RealizedSummary(
    int TotalSold,
    decimal TotalProceeds,
    decimal TotalCost,
    IReadOnlyList<RealizedLine> ByGame,
    decimal TotalFees = 0m,
    decimal TotalShippingCost = 0m,
    decimal TotalShippingCharged = 0m);
