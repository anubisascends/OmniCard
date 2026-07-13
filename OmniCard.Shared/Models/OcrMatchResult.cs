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
}
