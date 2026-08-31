using OmniCard.Models;

namespace OmniCard.Scanner;

// This partial lives in its own file (NOT linked into the dependency-light ScannerHost) because
// it references OmniCard.Models.ScanQuality. The host resolves DPI from CLI args instead, so it
// only needs the plain ScanSettings record + the applier — not this quality mapping.
public sealed partial record ScanSettings
{
    /// <summary>
    /// Resolve the user's quality choice and tuning knobs into the per-scan settings.
    /// <see cref="ScanQuality.Fast"/> maps to <paramref name="fastDpi"/>;
    /// <see cref="ScanQuality.HighQuality"/> maps to <paramref name="highQualityDpi"/>
    /// (0 = the scanner's native default resolution, applied by <see cref="ScanSettingsApplier"/>).
    /// </summary>
    public static ScanSettings Resolve(
        ScanQuality quality,
        bool foil,
        int fastDpi,
        int highQualityDpi,
        float foilBrightness,
        float foilContrast)
        => new(
            Dpi: quality == ScanQuality.Fast ? fastDpi : highQualityDpi,
            Foil: foil,
            FoilBrightness: foilBrightness,
            FoilContrast: foilContrast);
}
