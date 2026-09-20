namespace OmniCard.Shared.Sales;

public class ReceiptDocument
{
    // Company header
    public string? CompanyName { get; set; }
    public string? CompanyAddressBlock { get; set; }
    public string? CompanyLogoAbsolutePath { get; set; }
    /// <summary>Web-servable URL for the logo (e.g. "/branding/company-logo.png"); used by the HTML
    /// receipt view. The PDF exporter uses <see cref="CompanyLogoAbsolutePath"/> instead.</summary>
    public string? CompanyLogoUrl { get; set; }
    public string? CompanyEmail { get; set; }
    public string? CompanyPhone { get; set; }

    // Order info
    public string? OrderNumber { get; set; }
    /// <summary>Human-friendly sales channel the order came through (e.g. "TCGplayer", "eBay").</summary>
    public string? Channel { get; set; }
    public DateTime OrderDate { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }

    // Customer
    public string CustomerName { get; set; } = "";
    public string? CustomerAddressBlock { get; set; }

    // Lines + totals
    public IReadOnlyList<ReceiptLine> Lines { get; set; } = [];
    public bool ShowPrices { get; set; }
    public decimal ItemsTotal { get; set; }
    public decimal Shipping { get; set; }
    public decimal GrandTotal { get; set; }
    public string? FooterText { get; set; }

    // Layout
    public double WidthMm { get; set; }
    public double MarginMm { get; set; }
    public double FontPointSize { get; set; }
}

public class ReceiptLine
{
    public string Name { get; set; } = "";
    public string? Set { get; set; }
    public string? Condition { get; set; }
    public bool IsFoil { get; set; }
    public int Quantity { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal LineTotal { get; set; }
}
