namespace OmniCard.Models;

public class CsvImportPreview
{
    public CsvFormat DetectedFormat { get; init; }
    public List<CollectionCard> Cards { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public int TotalRows { get; init; }
}
