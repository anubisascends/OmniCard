namespace OmniCard.Models;

public class SalesSettings
{
    public int? ForSaleLocationId { get; set; }
    public CompanyProfile Company { get; set; } = new();
    public ReceiptSettings Receipt { get; set; } = new();

    /// <summary>Persisted width (px) of the Orders view's editor panel.</summary>
    public double? OrdersEditorWidth { get; set; }
    /// <summary>Whether the Orders view's editor panel is collapsed.</summary>
    public bool OrdersEditorCollapsed { get; set; }
}
