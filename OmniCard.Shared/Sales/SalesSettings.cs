namespace OmniCard.Shared.Sales;

public class SalesSettings
{
    public int? ForSaleLocationId { get; set; }

    /// <summary>Whether marking a listing as picked physically moves the card to
    /// <see cref="ForSaleLocationId"/>. When false, picking only flips the listing's status and
    /// leaves the card in its current location. Defaults to true (the historical behavior).</summary>
    public bool MovePickedToForSaleLocation { get; set; } = true;
    public CompanyProfile Company { get; set; } = new();
    public ReceiptSettings Receipt { get; set; } = new();

    /// <summary>Persisted width (px) of the Orders view's editor panel.</summary>
    public double? OrdersEditorWidth { get; set; }
    /// <summary>Whether the Orders view's editor panel is collapsed.</summary>
    public bool OrdersEditorCollapsed { get; set; }

    /// <summary>User-customized kanban lanes for the Sales/Orders board. Null or empty means the
    /// built-in defaults are used (see <see cref="WorkflowLane.Defaults"/>).</summary>
    public List<WorkflowLane>? WorkflowLanes { get; set; }
}
