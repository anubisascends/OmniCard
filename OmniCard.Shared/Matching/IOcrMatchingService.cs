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
    /// <summary>Every distinct (set code, collector number) the OCR passes read from the MTG bottom-left
    /// corner, best first (empty when nothing usable was read). The first entry is what
    /// <see cref="DetectMtgSetAndNumberAsync"/> returns; the rest are alternates for the catalog lookup to
    /// weigh against the image when the passes disagree. Default wraps the single best read so test doubles
    /// needn't implement it; the real service overrides it.</summary>
    async Task<(IReadOnlyList<MtgPrintedIdentity> Reads, double Confidence)> DetectMtgSetAndNumberCandidatesAsync(byte[] imageData)
    {
        var (set, number, confidence) = await DetectMtgSetAndNumberAsync(imageData);
        return set is null || number is null ? ([], 0) : ([new MtgPrintedIdentity(set, number, 1)], confidence);
    }
    /// <summary>Detects the MTG Planeswalker "hand" glyph printed in the bottom-left collector block of
    /// The List (plst) reprints. When present the card is a plst reprint even though its printed set code
    /// is the original set's — see WebScanMatchingService's MTG branch, which remaps the lookup to plst.
    /// Default no-op so test doubles needn't implement it; the real service overrides it.</summary>
    Task<(bool Present, double Confidence)> DetectMtgListSymbolAsync(byte[] imageData)
        => Task.FromResult((false, 0.0));
    /// <summary>OCR the Yu-Gi-Oh! lower-left edition line, returning "1st Edition"/"Limited Edition"
    /// when present, or null when no edition text is printed (Unlimited). Default no-op so test doubles
    /// and non-OCR implementations needn't implement it; the real service overrides it.</summary>
    Task<(string? Edition, double Confidence)> DetectYugiohEditionAsync(byte[] imageData)
        => Task.FromResult<(string?, double)>((null, 0));
    /// <summary>Reads the printing cues of an old-frame (pre-2015) MTG card — title, border colour and the
    /// bottom credit/copyright line — for when there is no set code + collector number to OCR. Default
    /// returns no evidence so test doubles needn't implement it; the real service overrides it.</summary>
    Task<MtgPrintEvidence> ReadMtgPrintEvidenceAsync(byte[] imageData)
        => Task.FromResult(MtgPrintEvidence.Empty);
    /// <summary>OCR of an old-frame MTG card's text box. Only requested when same-art candidate printings
    /// differ solely in flavor text (e.g. Alliances' a/b variants). Default null (no read).</summary>
    Task<string?> ReadMtgTextBoxAsync(byte[] imageData)
        => Task.FromResult<string?>(null);
    Dictionary<string, ulong> SymbolHashes { get; set; }
}
