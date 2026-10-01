namespace OmniCard.Shared.Matching;

/// <summary>One OCR read of the MTG bottom-left corner: the printed set code + collector number, and how
/// many of the OCR passes produced exactly this read. The collector line is tiny, so passes can disagree
/// on a digit (155 vs 185); the reads are ranked best-first and the catalog lookup settles disagreements
/// against the scan's image hash (see ScryfallService Phase 0). <see cref="Language"/> is the printed
/// language marker ("• JP") read alongside the set code, normalized to a CardLanguages code (null ⇒ unread).</summary>
public sealed record MtgPrintedIdentity(string SetCode, string CollectorNumber, int Votes, string? Language = null);
