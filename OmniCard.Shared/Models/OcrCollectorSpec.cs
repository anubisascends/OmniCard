namespace OmniCard.Models;

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

    /// <summary>The crop regions to try for the given orientation, honoring the multi-region lists
    /// when present and otherwise falling back to the single region.</summary>
    public IReadOnlyList<(double X, double Y, double W, double H)> RegionsFor(bool landscape) =>
        landscape
            ? (LandscapeRegions.Count > 0 ? LandscapeRegions : [LandscapeRegion])
            : (PortraitRegions.Count > 0 ? PortraitRegions : [PortraitRegion]);
}
