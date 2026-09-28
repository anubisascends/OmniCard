using OmniCard.Shared.Collection;
namespace OmniCard.Shared.ImportExport;

public class CsvImportPreview
{
    public CsvFormat DetectedFormat { get; init; }
    /// <summary>False when the header row matched none of the supported formats (no rows were read).</summary>
    public bool FormatRecognized { get; init; } = true;
    /// <summary>The file's header row, as read.</summary>
    public List<string> Headers { get; init; } = [];
    public List<CollectionCard> Cards { get; init; } = [];
    /// <summary>The spreadsheet row number (header = row 1) each entry of <see cref="Cards"/> came from.</summary>
    public List<int> CardRows { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    /// <summary>Per-row problems: rows that couldn't be parsed at all, plus values that were
    /// defaulted (an unrecognized condition read as NM, an unreadable quantity read as 1). The lenient
    /// import ignores these; the all-or-nothing location import rejects the file when any exist.</summary>
    public List<CsvRowIssue> Issues { get; init; } = [];
    public int TotalRows { get; init; }
}

/// <summary>A problem with one CSV row. <see cref="Row"/> is the spreadsheet row number (header = row 1).</summary>
public sealed record CsvRowIssue(int Row, string? CardName, string Message);
