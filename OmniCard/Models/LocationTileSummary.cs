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

    /// <summary>Distinct set codes present in this location, for displaying set symbols on the tile.</summary>
    public List<SetCodeRarity> SetSymbols { get; init; } = [];
}

public class SetCodeRarity
{
    public string SetCode { get; init; } = "";
    public string Rarity { get; init; } = "common";
}
