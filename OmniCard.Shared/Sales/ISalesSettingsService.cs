namespace OmniCard.Shared.Sales;

public interface ISalesSettingsService
{
    int? ForSaleLocationId { get; }
    void SetForSaleLocationId(int? id);

    /// <summary>Whether marking a listing as picked physically moves the card to the for-sale location
    /// (true, the default) or leaves it where it is (false).</summary>
    bool MovePickedToForSaleLocation { get; }
    void SetMovePickedToForSaleLocation(bool move);

    CompanyProfile GetCompany();
    void SaveCompany(CompanyProfile company);
    ReceiptSettings GetReceipt();
    void SaveReceipt(ReceiptSettings receipt);

    /// <summary>Copies the chosen image into the data directory and returns the stored
    /// path relative to the data directory (does not persist it — the caller assigns it
    /// to <see cref="CompanyProfile.LogoPath"/> and saves).</summary>
    string SetLogo(string sourcePath);

    /// <summary>Persisted width (px) of the Orders view editor panel (null = use default).</summary>
    double? OrdersEditorWidth { get; }
    void SetOrdersEditorWidth(double width);

    /// <summary>Whether the Orders view editor panel is collapsed.</summary>
    bool OrdersEditorCollapsed { get; }
    void SetOrdersEditorCollapsed(bool collapsed);

    /// <summary>The customizable Sales/Orders kanban lanes, in board order. Returns the built-in
    /// defaults (<see cref="WorkflowLane.Defaults"/>) when nothing has been saved yet — never
    /// empty.</summary>
    IReadOnlyList<WorkflowLane> GetWorkflowLanes();

    /// <summary>Persists the kanban lane configuration (board order preserved).</summary>
    void SaveWorkflowLanes(IEnumerable<WorkflowLane> lanes);
}
