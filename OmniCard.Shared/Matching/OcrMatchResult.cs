namespace OmniCard.Shared.Matching;

public class OcrMatchResult
{
    public string? RecognizedName { get; init; }
    public double NameConfidence { get; init; }
    public List<string> CandidateSetCodes { get; init; } = [];
    public double SymbolConfidence { get; init; }

    /// <summary>Collector number detected via OCR (e.g. "OP15-043"). Used for OPTCG direct lookup.</summary>
    public string? CollectorNumber { get; init; }
    public double CollectorNumberConfidence { get; init; }

    /// <summary>OPTCG only: the raw OCR reads of the collector-number line. OptcgService Phase 0 snaps
    /// them to catalog numbers (OptcgCollectorNumberResolver) and lets the scan's image hash settle
    /// disagreements and the alt-art variant. Considered alongside <see cref="CollectorNumber"/>.</summary>
    public IReadOnlyList<string> CollectorTexts { get; init; } = [];

    /// <summary>Set code read via OCR (e.g. "MKC"). For MTG the collector number is not unique on its
    /// own, so the (SetCode, CollectorNumber) pair is what identifies a printing — see
    /// ScryfallService.FindClosestMatch Phase 0. Null for games that look up by collector number alone.</summary>
    public string? SetCode { get; init; }

    /// <summary>MTG only: other (set, collector) reads the OCR passes disagreed on, besides
    /// <see cref="SetCode"/>/<see cref="CollectorNumber"/>. ScryfallService Phase 0 resolves each and lets the
    /// scan's image hash pick between them, so one misread digit can't pin the wrong printing.</summary>
    public IReadOnlyList<MtgPrintedIdentity> AlternateSetNumbers { get; init; } = [];

    /// <summary>The card language read from the print (MTG's "• JP" marker), as a canonical
    /// <see cref="OmniCard.Shared.Games.CardLanguages"/> code. When several catalog rows share the read
    /// (set, collector) — one per downloaded language — this picks the matching one. Null ⇒ unread.</summary>
    public string? Language { get; init; }
}
