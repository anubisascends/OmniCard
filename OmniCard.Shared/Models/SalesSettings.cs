namespace OmniCard.Models;

public class SalesSettings
{
    public int? ForSaleLocationId { get; set; }
    public CompanyProfile Company { get; set; } = new();
    public ReceiptSettings Receipt { get; set; } = new();
}
