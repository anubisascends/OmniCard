namespace OmniCard.Models;

/// <summary>
/// Controls what happens to scan images after <c>CardService.CommitScans</c> commits the DB
/// record: <see cref="Store"/> keeps a permanent copy under the scans directory (default,
/// existing behavior); <see cref="Discard"/> deletes the temp scan image once the commit
/// succeeds and never writes a permanent copy.
/// </summary>
public enum ScanWorkflowMode
{
    /// <summary>Keep a permanent copy of every scanned card's image under the scans directory
    /// after commit, linked to its inventory lot via <c>InventoryLot.ScanImagePath</c>.</summary>
    Store = 0,

    /// <summary>Delete each scan's temp image once its card has been committed to the
    /// database — no permanent copy is kept and <c>InventoryLot.ScanImagePath</c> stays null.</summary>
    Discard = 1,
}
