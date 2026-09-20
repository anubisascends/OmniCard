namespace OmniCard.Shared.ImportExport;

public class OrderImportPreview
{
    public List<OrderImportRow> Rows { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
