namespace OmniCard.Shared.Matching;

/// <summary>
/// What an MTG scan shows about its printing when there is no printed (set code, collector number) to
/// read — i.e. pre-2015 frames, where OCR-first matching can't apply. Same-art reprints (Unlimited /
/// Revised / 4th Edition, Arabian Nights / Chronicles, …) hash identically, so the printing is told apart
/// by the physical cues these carry: the title, the border colour, and the bottom credit/copyright line.
/// Raw OCR text is kept (not parsed here) so the catalog side can interpret it against each candidate
/// printing (e.g. spotting the candidate's artist in the credit line). See ScryfallService.ResolveOldFramePrinting.
/// </summary>
public sealed record MtgPrintEvidence
{
    /// <summary>OCR reads of the title bar under different preprocessing, in no particular order.</summary>
    public IReadOnlyList<string> TitleReads { get; init; } = [];

    /// <summary>"white" or "black" when the border was clearly one or the other; null when unclear.</summary>
    public string? BorderColor { get; init; }

    /// <summary>OCR reads of the bottom band (illustrator credit, copyright line, and on 1999+ old frames
    /// the "nnn/ttt" collector number).</summary>
    public IReadOnlyList<string> BottomLineReads { get; init; } = [];

    public static MtgPrintEvidence Empty { get; } = new();
}
