namespace OmniCard.Models;

/// <summary>
/// Captures the internal decision data from FindClosestMatch for diagnostic logging.
/// </summary>
public class MatchDiagnostics
{
    /// <summary>Which phase decided the match: ExactCorrection, PHashConfident, OcrAssisted, ArtHashFallback, NoMatch.</summary>
    public string DecisionPhase { get; set; } = "NoMatch";

    public int PHashDistance { get; set; }
    public int? ArtHashDistance { get; set; }
    public List<TieZoneCandidate> TieZoneCandidates { get; set; } = [];

    // OCR data (copied from OcrMatchResult for self-contained diagnostics)
    public string? OcrRecognizedName { get; set; }
    public double? OcrNameConfidence { get; set; }
    public List<OcrSetDetection>? OcrDetectedSets { get; set; }

    // Set filter state
    public bool SetFilterActive { get; set; }
    public List<string>? ActiveSets { get; set; }
    public List<string>? PreferredSets { get; set; }
}

public class TieZoneCandidate
{
    public string CardId { get; set; } = "";
    public string Name { get; set; } = "";
    public string SetCode { get; set; } = "";
    public string CollectorNumber { get; set; } = "";
    public int PHashDistance { get; set; }
    public int? ArtHashDistance { get; set; }
    public int SetBonus { get; set; }
    public int FinalScore { get; set; }
    public bool Selected { get; set; }
}

public class OcrSetDetection
{
    public string SetCode { get; set; } = "";
    public double Confidence { get; set; }
}
