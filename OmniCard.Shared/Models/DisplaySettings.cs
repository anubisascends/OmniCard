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
}
