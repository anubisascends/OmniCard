namespace OmniCard.Models;

/// <summary>
/// Per-scanner saved settings: OmniCard's DPI/foil tuning plus any user-set TWAIN capabilities.
/// DPI/foil values are nullable — a null falls back to the shared ScanSettings defaults, so a
/// brand-new profile changes nothing until the user overrides something. This is a plain POCO
/// (no NTwain dependency) and is linked into the ScannerHost so the out-of-process path can
/// deserialize and apply the same profile the in-process path uses.
/// </summary>
public sealed class ScannerProfile
{
    /// <summary>Filesystem/dictionary-safe key derived from <see cref="ScannerName"/>.</summary>
    public string ScannerKey { get; set; } = "";

    /// <summary>The raw TWAIN source name (may contain spaces/colons; kept for display).</summary>
    public string ScannerName { get; set; } = "";

    /// <summary>DPI for Fast quality. Null = use the shared default.</summary>
    public int? FastDpi { get; set; }

    /// <summary>DPI for High-Quality. Null = default; a stored 0 means the scanner's native default.</summary>
    public int? HighQualityDpi { get; set; }

    /// <summary>Brightness applied in foil mode. Null = use the shared default.</summary>
    public double? FoilBrightness { get; set; }

    /// <summary>Contrast applied in foil mode. Null = use the shared default.</summary>
    public double? FoilContrast { get; set; }

    /// <summary>Arbitrary user-set TWAIN capabilities, applied on top of OmniCard's baseline.</summary>
    public List<ScannerCapabilitySetting> Capabilities { get; set; } = [];
}
