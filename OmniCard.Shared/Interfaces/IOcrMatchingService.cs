using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IOcrMatchingService
{
    Task<OcrMatchResult> AnalyzeCardAsync(byte[] imageData);
    /// <summary>Synchronous set symbol detection only — no OCR. Fast enough for the scan pipeline.</summary>
    (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] imageData);
    /// <summary>OCR the collector number from an OPTCG card (e.g. "OP15-043").</summary>
    Task<(string? CollectorNumber, double Confidence)> DetectOptcgCollectorNumberAsync(byte[] imageData);
    /// <summary>OCR the collector line from a Riftbound card, returning "{SET}-{number}" (e.g. "UNL-150").</summary>
    Task<(string? CollectorNumber, double Confidence)> DetectRiftboundCollectorNumberAsync(byte[] imageData);
    /// <summary>OCR a collector number using a per-game crop/regex spec (Pokémon, Yu-Gi-Oh!, FFTCG).</summary>
    Task<(string? CollectorNumber, double Confidence)> DetectCollectorNumberAsync(byte[] imageData, OcrCollectorSpec spec);
    Dictionary<string, ulong> SymbolHashes { get; set; }
}
