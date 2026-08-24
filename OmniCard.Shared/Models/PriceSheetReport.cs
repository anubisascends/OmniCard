namespace OmniCard.Models;

public class PriceSheetReport
{
    public required string LocationName { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.Now;

    /// <summary>All cards in the location, flattened across every game and sorted by card
    /// name (ascending, case-insensitive).</summary>
    public List<PriceSheetLine> Lines { get; init; } = [];
}

public class PriceSheetLine
{
    public required string Name { get; init; }
    public required string GameDisplayName { get; init; }
    public string? SetCode { get; init; }
    public string? CollectorNumber { get; init; }
    public required decimal Price { get; init; }

    /// <summary>The card's identifier formatted as [SET CODE]-[COLLECTOR NUMBER]
    /// (e.g. "LEA-1"). Falls back to just the set code when there is no collector
    /// number (sealed product), or an empty string when neither is present.</summary>
    public string CardCode
    {
        get
        {
            var set = string.IsNullOrWhiteSpace(SetCode) ? null : SetCode.Trim().ToUpperInvariant();
            var number = string.IsNullOrWhiteSpace(CollectorNumber) ? null : CollectorNumber.Trim();

            return (set, number) switch
            {
                (not null, not null) => $"{set}-{number}",
                (not null, null) => set,
                (null, not null) => number,
                _ => "",
            };
        }
    }
}
