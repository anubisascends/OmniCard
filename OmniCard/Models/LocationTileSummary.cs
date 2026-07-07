namespace OmniCard.Models;

public class LocationTileSummary
{
    public required StorageContainer Container { get; init; }
    public int CardCount { get; init; }
    public decimal TotalMarketValue { get; init; }
    public decimal TotalPurchaseCost { get; init; }
    public decimal PriceDelta { get; init; }
    public double PriceDeltaPercent { get; init; }
    public string? CoverImageUri { get; init; }
}
