namespace OmniCard.Models;

public class OcrMatchResult
{
    public string? RecognizedName { get; init; }
    public double NameConfidence { get; init; }
    public List<string> CandidateSetCodes { get; init; } = [];
    public double SymbolConfidence { get; init; }

    /// <summary>Collector number detected via OCR (e.g. "OP15-043"). Used for OPTCG direct lookup.</summary>
    public string? CollectorNumber { get; init; }
    public double CollectorNumberConfidence { get; init; }

    /// <summary>Set code read via OCR (e.g. "MKC"). For MTG the collector number is not unique on its
    /// own, so the (SetCode, CollectorNumber) pair is what identifies a printing — see
    /// ScryfallService.FindClosestMatch Phase 0. Null for games that look up by collector number alone.</summary>
    public string? SetCode { get; init; }
}
