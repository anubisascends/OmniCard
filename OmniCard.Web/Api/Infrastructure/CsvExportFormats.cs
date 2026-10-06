using System.IO.Compression;
using OmniCard.Shared.Collection;
using OmniCard.Shared.ImportExport;

namespace OmniCard.Web.Api.Infrastructure;

/// <summary>
/// The export formats the SPA can request by key, shared by the collection export
/// (<c>ExportController</c>) and the scan-list export (<c>CardScanController</c>). An unknown or blank
/// key falls back to the app-native format.
/// </summary>
public static class CsvExportFormats
{
    public sealed record Format(
        string Key, string Extension, string ContentType,
        Action<ICsvExportImportService, string, IReadOnlyList<CollectionCard>> Write);

    private static readonly Format AppNative =
        new("appnative", ".csv", "text/csv", (s, p, c) => s.ExportAppNative(p, c));

    private static readonly Dictionary<string, Format> ByKey = new Format[]
    {
        AppNative,
        new("tcgplayer", ".csv", "text/csv", (s, p, c) => s.ExportTcgPlayer(p, c)),
        new("moxfield", ".csv", "text/csv", (s, p, c) => s.ExportMoxfield(p, c)),
        new("manabox", ".csv", "text/csv", (s, p, c) => s.ExportManabox(p, c)),
        new("ticker", ".csv", "text/csv", (s, p, c) => s.ExportPriceTicker(p, c)),
        new("archidekt", ".csv", "text/csv", (s, p, c) => s.ExportArchidekt(p, c)),
        new("deckbox", ".csv", "text/csv", (s, p, c) => s.ExportDeckbox(p, c)),
        new("dragonshield", ".csv", "text/csv", (s, p, c) => s.ExportDragonShield(p, c)),
        new("text", ".txt", "text/plain", (s, p, c) => s.ExportTextList(p, c)),
    }.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

    public static Format Resolve(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var format) ? format : AppNative;

    /// <summary>Writes <paramref name="cards"/> in one format, returning the file bytes.</summary>
    public static byte[] Produce(ICsvExportImportService csv, Format format, IReadOnlyList<CollectionCard> cards) =>
        TempFile.Produce(format.Extension, p => format.Write(csv, p, cards));

    /// <summary>Writes <paramref name="cards"/> once per format into a zip, each entry named
    /// <c>{baseName}-{key}{ext}</c>. Duplicate keys are written once.</summary>
    public static byte[] ProduceZip(
        ICsvExportImportService csv, IEnumerable<Format> formats, IReadOnlyList<CollectionCard> cards, string baseName)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var format in formats.DistinctBy(f => f.Key))
            {
                var entry = zip.CreateEntry($"{baseName}-{format.Key}{format.Extension}", CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(Produce(csv, format, cards));
            }
        }
        return ms.ToArray();
    }
}
