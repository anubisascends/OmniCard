namespace OmniCard.Models;

public class PriceSheetReport
{
    public required string LocationName { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
    public List<PriceSheetSection> Sections { get; init; } = [];
}

public class PriceSheetSection
{
    public required string GameDisplayName { get; init; }
    public List<PriceSheetLine> Lines { get; init; } = [];
}

public class PriceSheetLine
{
    public required string Name { get; init; }
    public string? SetCode { get; init; }
    public string? CollectorNumber { get; init; }
    public required decimal Price { get; init; }
}
