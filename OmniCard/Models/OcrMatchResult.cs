namespace OmniCard.Models;

public class OcrMatchResult
{
    public string? RecognizedName { get; init; }
    public double NameConfidence { get; init; }
    public List<string> CandidateSetCodes { get; init; } = [];
    public double SymbolConfidence { get; init; }
}
