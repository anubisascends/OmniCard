namespace OmniCard.Scanner;

/// <summary>
/// Resolved, hardware-agnostic scan-tuning knobs applied to a TWAIN source before each
/// scan. Shared by the in-process <see cref="ScannerService"/> and the out-of-process
/// ScannerHost (this file is linked into that project) so both paths behave identically.
/// </summary>
/// <param name="Dpi">Scan resolution. <c>0</c> means "use the source's native default resolution".</param>
/// <param name="Foil">Whether to apply the foil image tuning (auto-bright off, darker, higher contrast).</param>
/// <param name="FoilBrightness">Brightness applied in foil mode (TWAIN range is roughly -1000..1000).</param>
/// <param name="FoilContrast">Contrast applied in foil mode.</param>
public sealed partial record ScanSettings(
    int Dpi,
    bool Foil,
    float FoilBrightness,
    float FoilContrast)
{
    /// <summary>Default DPI for <c>ScanQuality.Fast</c> — the value OmniCard shipped before this was configurable.</summary>
    public const int DefaultFastDpi = 200;

    /// <summary>Default DPI for <c>ScanQuality.HighQuality</c>. <c>0</c> means the source's native default resolution.</summary>
    public const int DefaultHighQualityDpi = 0;

    /// <summary>Default brightness for foil mode.</summary>
    public const float DefaultFoilBrightness = -200f;

    /// <summary>Default contrast for foil mode.</summary>
    public const float DefaultFoilContrast = 333.3333f;
}
