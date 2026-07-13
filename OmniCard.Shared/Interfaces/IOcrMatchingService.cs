using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IOcrMatchingService
{
    Task<OcrMatchResult> AnalyzeCardAsync(byte[] imageData);
    /// <summary>Synchronous set symbol detection only — no OCR. Fast enough for the scan pipeline.</summary>
    (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] imageData);
    /// <summary>OCR the collector number from an OPTCG card (e.g. "OP15-043").</summary>
    Task<(string? CollectorNumber, double Confidence)> DetectOptcgCollectorNumberAsync(byte[] imageData);
    Dictionary<string, ulong> SymbolHashes { get; set; }
}
