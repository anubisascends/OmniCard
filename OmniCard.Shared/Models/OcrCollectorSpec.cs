namespace OmniCard.Models;

// Per-game configuration for collector-number OCR. Regions are fractions of the card image
// (X, Y, Width, Height). RegexPattern's first capture group is the normalized collector number.
public sealed class OcrCollectorSpec
{
    public (double X, double Y, double W, double H) PortraitRegion { get; init; }
    public (double X, double Y, double W, double H) LandscapeRegion { get; init; }
    public string Whitelist { get; init; } = "";
    public string RegexPattern { get; init; } = "";
}
