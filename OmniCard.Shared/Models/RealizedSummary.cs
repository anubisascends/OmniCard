namespace OmniCard.Models;

public record RealizedSummary(
    int TotalSold,
    decimal TotalProceeds,
    decimal TotalCost,
    IReadOnlyList<RealizedLine> ByGame);
