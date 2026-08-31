using System.Text;
using NTwain.Data;

namespace OmniCard.Scanner;

/// <summary>How a probed capability should be edited in the UI.</summary>
public enum CapKind
{
    /// <summary>On/off (TWAIN Bool).</summary>
    Bool,
    /// <summary>Pick from a fixed set of values (TWAIN Enumeration/Array).</summary>
    Enum,
    /// <summary>Numeric within min/max/step (TWAIN Range).</summary>
    Range,
    /// <summary>Free text / free numeric single value.</summary>
    Text,
}

/// <summary>
/// The result of probing one TWAIN capability on a connected scanner: enough to render an editor
/// and to persist the chosen value. Produced by <see cref="ScannerCapabilityProbe"/>.
/// </summary>
public sealed class ProbedCapability
{
    public required CapabilityId Cap { get; init; }
    public required string CapId { get; init; }
    public required string Label { get; init; }
    public required string Group { get; init; }

    /// <summary>Plain-English explanation of what the capability does (may be empty for obscure caps).</summary>
    public string Description { get; init; } = "";

    public required CapKind Kind { get; init; }
    public required string ItemType { get; init; }
    public object? Current { get; init; }
    public object? Default { get; init; }

    /// <summary>Allowed values for an enumeration-constrained cap, each with a raw (for the scanner)
    /// and a friendly display value (for the user).</summary>
    public IReadOnlyList<CapValueOption>? Options { get; init; }

    public decimal? RangeMin { get; init; }
    public decimal? RangeMax { get; init; }
    public decimal? RangeStep { get; init; }

    /// <summary>The scanner reports this cap as settable AND it isn't OmniCard-managed.</summary>
    public bool Settable { get; init; }

    /// <summary>OmniCard manages this cap for reliable matching — shown read-only.</summary>
    public bool Protected { get; init; }

    /// <summary>A proprietary vendor capability (id ≥ 0x8000) with no public documentation.
    /// Hidden by default in the UI.</summary>
    public bool IsVendorSpecific { get; init; }
}

/// <summary>An allowed value of an enumeration-constrained capability. <see cref="Value"/> is the raw
/// invariant string persisted and sent to the scanner; <see cref="Display"/> is the human-readable
/// meaning shown in the dropdown (e.g. Value "2" -> Display "Color (RGB)").</summary>
public sealed class CapValueOption
{
    public required string Value { get; init; }
    public required string Display { get; init; }
    public override string ToString() => Display;
}

/// <summary>
/// Human-readable meanings for the numeric values of common enumeration capabilities, from the TWAIN
/// spec. Lets the UI show "Color (RGB)" instead of "2". Keyed by capability name string then by the
/// numeric code, so it's safe (no enum-member references) and easy to extend.
/// </summary>
public static class CapabilityValueMeanings
{
    private static readonly Dictionary<string, Dictionary<long, string>> Map = new(StringComparer.Ordinal)
    {
        ["ICapPixelType"] = new() { [0] = "Black & White", [1] = "Grayscale", [2] = "Color (RGB)", [3] = "Palette", [4] = "CMY", [5] = "CMYK", [6] = "YUV", [7] = "YUVK", [8] = "CIE XYZ", [9] = "L*a*b*", [10] = "sRGB", [11] = "scRGB", [16] = "Infrared" },
        ["ICapUnits"] = new() { [0] = "Inches", [1] = "Centimeters", [2] = "Picas", [3] = "Points", [4] = "Twips", [5] = "Pixels", [6] = "Millimeters" },
        ["ICapBitOrder"] = new() { [0] = "LSB first", [1] = "MSB first" },
        ["ICapPixelFlavor"] = new() { [0] = "Chocolate (0 = black)", [1] = "Vanilla (0 = white)" },
        ["ICapOrientation"] = new() { [0] = "Portrait (0°)", [1] = "90°", [2] = "180°", [3] = "270°", [4] = "Auto", [5] = "Auto (text)", [6] = "Auto (picture)" },
        ["ICapLightSource"] = new() { [0] = "Red", [1] = "Green", [2] = "Blue", [3] = "None", [4] = "White", [5] = "Ultraviolet", [6] = "Infrared" },
        ["ICapLightPath"] = new() { [0] = "Reflective", [1] = "Transmissive" },
        ["CapDuplex"] = new() { [0] = "None", [1] = "1-pass duplex", [2] = "2-pass duplex" },
        ["CapCameraSide"] = new() { [0] = "Both", [1] = "Top", [2] = "Bottom" },
        ["ICapPlanarChunky"] = new() { [0] = "Chunky (interleaved)", [1] = "Planar (separate channels)" },
        ["ICapXferMech"] = new() { [0] = "Native", [1] = "File", [2] = "Memory", [4] = "Memory-file" },
        ["ICapCompression"] = new() { [0] = "None", [1] = "PackBits", [2] = "CCITT Group 3 (1D)", [3] = "CCITT Group 3 (1D EOL)", [4] = "CCITT Group 3 (2D)", [5] = "CCITT Group 4", [6] = "JPEG", [7] = "LZW", [8] = "JBIG", [9] = "PNG", [10] = "RLE4", [11] = "RLE8", [13] = "ZIP", [14] = "JPEG 2000" },
        ["ICapImageFileFormat"] = new() { [0] = "TIFF", [1] = "PICT", [2] = "BMP", [3] = "XBM", [4] = "JPEG (JFIF)", [5] = "FlashPix", [6] = "Multi-page TIFF", [7] = "PNG", [8] = "SPIFF", [9] = "EXIF", [10] = "PDF", [11] = "JPEG 2000", [15] = "PDF/A", [16] = "PDF/A-2" },
        ["ICapSupportedSizes"] = new() { [0] = "None", [1] = "A4", [2] = "JIS B5", [3] = "US Letter", [4] = "US Legal", [5] = "A5" },
    };

    /// <summary>Friendly meaning for a numeric capability value, or null if unknown.</summary>
    public static string? Describe(string capId, long code)
        => Map.TryGetValue(capId, out var m) && m.TryGetValue(code, out var name) ? name : null;
}

/// <summary>Friendly label, group, and a plain-English description for each capability id. Keyed by
/// the capability's <em>name string</em> (not the enum member) so coverage can be broad without any
/// risk of referencing a member that doesn't exist in this NTwain version. Unmapped caps fall back
/// to a prettified name (e.g. "ICapAutomaticDeskew" -> "Automatic Deskew"), an "Other" group, and no
/// description.</summary>
public static class CapabilityLabels
{
    private static readonly Dictionary<string, (string Label, string Group, string Description)> Meta =
        new(StringComparer.Ordinal)
    {
        // --- Image ---
        ["ICapBrightness"] = ("Brightness", "Image", "Overall lightness of the scan. Higher values make the whole image brighter."),
        ["ICapContrast"] = ("Contrast", "Image", "Difference between light and dark areas. Higher is punchier; lower is flatter."),
        ["ICapGamma"] = ("Gamma", "Image", "Midtone brightness curve — brightens or darkens the middle tones without clipping pure black or white."),
        ["ICapHighlight"] = ("Highlight", "Image", "The input level treated as pure white. Lower it to recover detail in blown-out bright areas."),
        ["ICapShadow"] = ("Shadow", "Image", "The input level treated as pure black. Raise it to deepen or recover detail in dark areas."),
        ["ICapAutoBright"] = ("Auto Brightness", "Image", "Let the scanner adjust brightness automatically per page. Turn off for consistent, manual control."),
        ["ICapNoiseFilter"] = ("Noise Filter", "Image", "Removes speckle and graininess. Cleans up scans but can soften fine detail."),
        ["ICapExposureTime"] = ("Exposure Time", "Image", "How long the sensor is exposed per line. Longer is brighter but slower."),
        ["ICapThreshold"] = ("B&W Threshold", "Image", "For black & white scans, the gray level where a pixel becomes black rather than white."),
        ["ICapJpegQuality"] = ("JPEG Quality", "Image", "Compression quality when saving as JPEG. Higher keeps more detail but makes larger files."),

        // --- Color ---
        ["ICapPixelType"] = ("Color Mode", "Color", "Black & white, grayscale, or full color. OmniCard needs color for card matching, so this is locked."),
        ["ICapBitDepth"] = ("Bit Depth", "Color", "Bits per pixel (color depth). Higher gives more shades/colors and larger files."),
        ["ICapPixelFlavor"] = ("Pixel Flavor", "Color", "For black & white images, whether 0 means black or white. Rarely needs changing."),
        ["ICapBitOrder"] = ("Bit Order", "Color", "Bit ordering within each byte for black & white images. Rarely needs changing."),
        ["ICapLightSource"] = ("Light Source", "Color", "Which lamp/color channel the scanner illuminates with (e.g. white, red, green, blue)."),
        ["ICapICCProfile"] = ("ICC Profile", "Color", "How color-profile data is embedded in the image. OmniCard manages this for accurate color, so it's locked."),

        // --- Resolution ---
        ["ICapXResolution"] = ("Resolution X (DPI)", "Resolution", "Horizontal scan resolution in dots per inch. Higher captures more detail and produces larger files."),
        ["ICapYResolution"] = ("Resolution Y (DPI)", "Resolution", "Vertical scan resolution in dots per inch. Higher captures more detail and produces larger files."),
        ["ICapXNativeResolution"] = ("Native Resolution X", "Resolution", "The scanner's true optical resolution across the page — its hardware detail limit (read-only)."),
        ["ICapYNativeResolution"] = ("Native Resolution Y", "Resolution", "The scanner's true optical resolution down the page — its hardware detail limit (read-only)."),

        // --- Geometry ---
        ["ICapOrientation"] = ("Orientation", "Geometry", "The page orientation (portrait or landscape) the scanner assumes."),
        ["ICapRotation"] = ("Rotation", "Geometry", "Rotate the scanned image by a fixed angle."),
        ["ICapFlipRotation"] = ("Flip Rotation", "Geometry", "Flip the rotation direction — useful when pages feed in upside-down."),
        ["ICapMirror"] = ("Mirror", "Geometry", "Mirror the image horizontally or vertically."),
        ["ICapUnits"] = ("Measurement Units", "Geometry", "Unit used for scan-area measurements (inches, centimeters, or pixels)."),
        ["ICapOverScan"] = ("Overscan", "Geometry", "Scan slightly beyond the page edge so nothing near the border gets clipped."),
        ["ICapAutomaticDeskew"] = ("Auto Deskew", "Geometry", "Automatically straightens pages that fed in crooked."),
        ["ICapAutomaticBorderDetection"] = ("Auto Border Detection", "Geometry", "Detects the page edges and crops the scan to them."),
        ["ICapAutomaticRotate"] = ("Auto Rotate", "Geometry", "Rotates each page to its upright orientation automatically."),
        ["ICapUndefinedImageSize"] = ("Variable Page Length", "Geometry", "Allows pages of unknown length — the scanner captures until the page ends."),
        ["ICapSupportedSizes"] = ("Paper Size", "Geometry", "Preset paper sizes the scanner can auto-crop the scan to."),

        // --- Feeder & Paper ---
        ["CapAutoScan"] = ("Auto Scan (ADF)", "Feeder & Paper", "Keeps pulling pages from the document feeder automatically. OmniCard needs this on for feeder scanners, so it's locked."),
        ["CapDuplex"] = ("Duplex Capability", "Feeder & Paper", "Whether the scanner can scan both sides at once (read-only capability report)."),
        ["CapDuplexEnabled"] = ("Duplex Enabled", "Feeder & Paper", "Scan both sides of a page. OmniCard scans single-sided for cards, so this is locked."),
        ["CapFeederEnabled"] = ("Use Document Feeder", "Feeder & Paper", "Use the automatic document feeder instead of the flatbed glass."),
        ["CapFeederLoaded"] = ("Feeder Loaded", "Feeder & Paper", "Whether paper is currently loaded in the feeder (read-only)."),
        ["CapPaperDetectable"] = ("Paper Detectable", "Feeder & Paper", "Whether the scanner can sense that paper is present (read-only)."),
        ["ICapAutoDiscardBlankPages"] = ("Discard Blank Pages", "Feeder & Paper", "Automatically skips and drops blank pages during a batch."),
        ["CapCameraSide"] = ("Camera Side", "Feeder & Paper", "Which side/camera (front or back) a setting applies to on duplex scanners."),

        // --- Transfer (mostly OmniCard-managed) ---
        ["ICapXferMech"] = ("Transfer Mechanism", "Transfer", "How images are transferred from the scanner (memory, native, or file). OmniCard manages this, so it's locked."),
        ["ICapCompression"] = ("Compression", "Transfer", "Compression applied to images during transfer. OmniCard manages this to keep images intact, so it's locked."),
        ["ICapImageFileFormat"] = ("Image File Format", "Transfer", "The file format the scanner writes when saving directly to file. OmniCard manages this, so it's locked."),
        ["ICapPlanarChunky"] = ("Color Layout", "Transfer", "How color channels are arranged in memory. OmniCard manages this, so it's locked."),
        ["CapXferCount"] = ("Transfer Count", "Transfer", "How many images to transfer in one session. OmniCard pins this so batches aren't cut short, so it's locked."),
        ["ICapImageDataSet"] = ("Image Selection", "Transfer", "Selects which image(s) to keep from a multi-image scan."),
    };

    /// <summary>TWAIN custom/vendor capability ids start at 0x8000 (CAP_CUSTOMBASE). They are not part of
    /// the standard and are proprietary to the scanner's own driver, so there is no public name or
    /// documentation for them (NTwain reports them by number).</summary>
    public static bool IsCustom(CapabilityId cap) => (ushort)cap >= 0x8000;

    private const string CustomDescription =
        "Vendor-specific capability — not part of the TWAIN standard. Its meaning is defined only by this " +
        "scanner's driver and isn't publicly documented, so OmniCard can't describe it. Changing it can have " +
        "unpredictable effects; leave it alone unless the manufacturer's own documentation explains it.";

    public static string LabelFor(CapabilityId cap)
    {
        if (Meta.TryGetValue(cap.ToString(), out var m) && m.Label.Length > 0) return m.Label;
        return IsCustom(cap) ? $"Custom 0x{(ushort)cap:X4}" : Prettify(cap.ToString());
    }

    public static string GroupFor(CapabilityId cap)
    {
        if (Meta.TryGetValue(cap.ToString(), out var m)) return m.Group;
        return IsCustom(cap) ? "Vendor-specific (advanced)" : "Other";
    }

    public static string DescriptionFor(CapabilityId cap)
    {
        if (Meta.TryGetValue(cap.ToString(), out var m)) return m.Description;
        return IsCustom(cap) ? CustomDescription : "";
    }

    private static string Prettify(string name)
    {
        var s = name;
        if (s.StartsWith("ICap", StringComparison.Ordinal)) s = s[4..];
        else if (s.StartsWith("Cap", StringComparison.Ordinal)) s = s[3..];

        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1])) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
