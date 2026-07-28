using System.IO;

namespace OmniCard.Collection;

public enum ImportKind { Csv, Decklist, Unknown }

/// <summary>Sniffs a file (or its first content line) to route imports to the right dialog.</summary>
public static class ImportFileClassifier
{
    public static ImportKind Classify(string firstContentLine)
    {
        if (string.IsNullOrWhiteSpace(firstContentLine))
            return ImportKind.Unknown;

        var headers = new HashSet<string>(
            firstContentLine.Split(',').Select(h => h.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (CsvExportImportService.DetectFormat(headers) is not null)
            return ImportKind.Csv;

        if (DecklistService.LooksLikeDecklistLine(firstContentLine))
            return ImportKind.Decklist;

        return ImportKind.Unknown;
    }

    /// <summary>Reads the first non-ignorable line of the file and classifies it. Unknown on empty/unreadable content.</summary>
    public static ImportKind ClassifyFile(string path)
    {
        foreach (var raw in File.ReadLines(path))
        {
            if (DecklistService.IsIgnorableLine(raw))
                continue;
            return Classify(raw.Trim());
        }
        return ImportKind.Unknown;
    }
}
