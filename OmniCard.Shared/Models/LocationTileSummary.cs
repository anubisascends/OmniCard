namespace OmniCard.Models;

public class LocationTileSummary
{
    public required StorageContainer Container { get; init; }

    /// <summary>Total physical cards in the location (including duplicates).</summary>
    public int CardCount { get; init; }

    /// <summary>Distinct card printings in the location (distinct GameCardId).</summary>
    public int UniquePrintCount { get; init; }

    public decimal TotalMarketValue { get; init; }
    public decimal TotalPurchaseCost { get; init; }
    public decimal PriceDelta { get; init; }
    public double PriceDeltaPercent { get; init; }
    public string? CoverImageUri { get; init; }
}
