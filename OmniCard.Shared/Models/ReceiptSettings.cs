namespace OmniCard.Models;

public class ReceiptSettings
{
    public double WidthMm { get; set; } = 80;
    public double MarginMm { get; set; } = 4;
    public double FontPointSize { get; set; } = 9;
    public bool ShowPrices { get; set; } = true;
    public string? FooterText { get; set; }
    public string? DefaultPrinterName { get; set; }
}
