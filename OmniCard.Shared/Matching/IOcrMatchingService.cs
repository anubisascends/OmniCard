namespace OmniCard.Shared.Matching;

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
    /// <summary>OCR the modern MTG bottom-left corner, returning the set code (e.g. "MKC") and collector
    /// number (e.g. "66"). Both are needed to identify a printing; either being null means the read
    /// isn't usable for a ground-truth lookup (e.g. pre-2015 cards that print neither).</summary>
    Task<(string? SetCode, string? CollectorNumber, double Confidence)> DetectMtgSetAndNumberAsync(byte[] imageData);
    /// <summary>OCR the Yu-Gi-Oh! lower-left edition line, returning "1st Edition"/"Limited Edition"
    /// when present, or null when no edition text is printed (Unlimited). Default no-op so test doubles
    /// and non-OCR implementations needn't implement it; the real service overrides it.</summary>
    Task<(string? Edition, double Confidence)> DetectYugiohEditionAsync(byte[] imageData)
        => Task.FromResult<(string?, double)>((null, 0));
    Dictionary<string, ulong> SymbolHashes { get; set; }
}
