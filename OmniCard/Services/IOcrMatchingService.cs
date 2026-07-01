using OmniCard.Models;

namespace OmniCard.Services;

public interface IOcrMatchingService
{
    Task<OcrMatchResult> AnalyzeCardAsync(byte[] imageData);
    /// <summary>Synchronous set symbol detection only — no OCR. Fast enough for the scan pipeline.</summary>
    (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] imageData);
    Dictionary<string, ulong> SymbolHashes { get; set; }
}
