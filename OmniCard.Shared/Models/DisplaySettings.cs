namespace OmniCard.Models;

public class DisplaySettings
{
    public double CardDetailFontSize { get; set; } = 14;
    public string Theme { get; set; } = "Dark";
    public double CardPreviewScale { get; set; } = 100;
    public Dictionary<string, bool> CollectionColumnVisibility { get; set; } = new();
    public bool StackDuplicates { get; set; }
    public double ScannerFontSize { get; set; } = 14;
    public double ScannerListWidth { get; set; }
    public string? DefaultScannerName { get; set; }
    public ScanQuality ScanQuality { get; set; } = ScanQuality.Fast;
    public bool ShowScannerUI { get; set; }
    public bool SidebarExpanded { get; set; } = true;

    /// <summary>Width (px) of the Collection card-list's details panel.</summary>
    public double CardDetailsSidebarWidth { get; set; } = 300;

    /// <summary>Whether the Collection card-list's details panel is collapsed by the user.</summary>
    public bool CardDetailsSidebarCollapsed { get; set; }

    /// <summary>Scan tiles at/above this price get the non-bulk (green by default) right-edge accent.</summary>
    public decimal NonBulkPriceThreshold { get; set; } = 1.00m;

    /// <summary>Scan tiles at/above this price get the high-value (blue by default) right-edge accent,
    /// taking precedence over <see cref="NonBulkPriceThreshold"/>.</summary>
    public decimal HighValuePriceThreshold { get; set; } = 20.00m;

    /// <summary>Hex color (e.g. "#4CAF50") for the non-bulk price-tier accent.</summary>
    public string NonBulkIndicatorColor { get; set; } = "#4CAF50";

    /// <summary>Hex color (e.g. "#2196F3") for the high-value price-tier accent.</summary>
    public string HighValueIndicatorColor { get; set; } = "#2196F3";
}
