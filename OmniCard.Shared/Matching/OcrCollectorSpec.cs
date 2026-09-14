namespace OmniCard.Shared.Matching;

/// <summary>Provider-neutral Tesseract page-segmentation mode for a collector-code crop. Kept out of
/// OmniCard.Imaging so OmniCard.Shared doesn't take a Tesseract dependency; mapped to the engine's
/// <c>PageSegMode</c> in <c>OcrMatchingService</c>.</summary>
public enum OcrPageSegMode
{
    /// <summary>Legacy behaviour: SingleBlock when <see cref="OcrCollectorSpec.MultiLine"/>, else SingleLine.</summary>
    Default,
    SingleLine,
    SingleBlock,
    /// <summary>Sparse text — find as much text as possible in no particular order. Far more robust than
    /// SingleLine on short, wide code strips that carry a bit of border/frame noise (Yu-Gi-Oh! set codes).</summary>
    SparseText,
}

// Per-game configuration for collector-number OCR. Regions are fractions of the card image
// (X, Y, Width, Height). RegexPattern's first capture group is the normalized collector number.
public sealed class OcrCollectorSpec
{
    public (double X, double Y, double W, double H) PortraitRegion { get; init; }
    public (double X, double Y, double W, double H) LandscapeRegion { get; init; }
    public string Whitelist { get; init; } = "";
    public string RegexPattern { get; init; } = "";

    /// <summary>Optional extra crop regions tried in order — e.g. the set code prints in different
    /// spots for different card layouts (Yu-Gi-Oh! Spell/Trap vs Monster). When non-empty these are
    /// used instead of the single <see cref="PortraitRegion"/>/<see cref="LandscapeRegion"/>.</summary>
    public IReadOnlyList<(double X, double Y, double W, double H)> PortraitRegions { get; init; } = [];
    public IReadOnlyList<(double X, double Y, double W, double H)> LandscapeRegions { get; init; } = [];

    /// <summary>Otsu-binarize / high-contrast the crop before OCR. Helps small, low-contrast
    /// holofoil set-code text (Yu-Gi-Oh!) at the cost of a little extra work per scan.</summary>
    public bool Binarize { get; init; }

    /// <summary>Return the best code-like token loosely (letters+digits, separators stripped) even
    /// when it doesn't strictly match <see cref="RegexPattern"/> — for downstream fuzzy matching
    /// against the catalog, which tolerates OCR character confusions.</summary>
    public bool LooseExtraction { get; init; }

    /// <summary>Page-segmentation mode for the crop. Defaults to legacy single-line/single-block.
    /// Yu-Gi-Oh! uses <see cref="OcrPageSegMode.SparseText"/> — its set-code strips read as garbage
    /// under SingleLine.</summary>
    public OcrPageSegMode PageSegMode { get; init; } = OcrPageSegMode.Default;

    /// <summary>Allow a loose token that has letters but NO digits (e.g. "DAMA-ENULZ", where a
    /// holofoil read turns "012" into letters). Only meaningful with <see cref="LooseExtraction"/> and
    /// a confusion-aware fuzzy catalog match downstream, which maps the letters back to digits. Off by
    /// default so digit-free noise can't win for games that read cleanly.</summary>
    public bool AllowLetterOnlyToken { get; init; }

    /// <summary>OCR the crop as a multi-line text block rather than a single line. Use when the code
    /// shares a tall crop with neighbouring text (FFTCG prints the set code on the bottom credit line,
    /// directly below the illustrator/copyright lines); a taller block crop is robust to the code's
    /// exact vertical position drifting card-to-card, and <see cref="RegexPattern"/> isolates the code
    /// from the surrounding credit text.</summary>
    public bool MultiLine { get; init; }

    /// <summary>The crop regions to try for the given orientation, honoring the multi-region lists
    /// when present and otherwise falling back to the single region.</summary>
    public IReadOnlyList<(double X, double Y, double W, double H)> RegionsFor(bool landscape) =>
        landscape
            ? (LandscapeRegions.Count > 0 ? LandscapeRegions : [LandscapeRegion])
            : (PortraitRegions.Count > 0 ? PortraitRegions : [PortraitRegion]);
}
