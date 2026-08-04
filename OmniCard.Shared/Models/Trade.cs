namespace OmniCard.Models;

/// <summary>Permanent record of a trade, independent of whether the traded-away
/// <see cref="InventoryLot"/> still exists — created by <c>TradeImportService</c> from a web-app
/// <see cref="TradeRecord"/>, and outlives the lot once a replacement scan is committed and the
/// original lot is deleted (see <see cref="InventoryLot.FulfilledTradeId"/>).</summary>
public class Trade
{
    public int Id { get; set; }

    /// <summary>Ties back to the <c>trades/{TradeRecordId}/</c> folder for photo lookup.</summary>
    public Guid TradeRecordId { get; set; }

    // Snapshot of the traded-away card, copied from TradeRecord at import time.
    public CardGame Game { get; set; }
    public string CardName { get; set; } = "";
    public string? SetCode { get; set; }
    public string? SetName { get; set; }
    public string? CollectorNumber { get; set; }
    public bool Foil { get; set; }

    public string Note { get; set; } = "";
    public string? PhotoPath { get; set; }

    /// <summary>The lot that was traded away. Cleared (set null) once a linked replacement is
    /// committed and the lot is deleted — the trade itself is kept regardless, so this only
    /// tracks whether the removal side has happened yet, not whether the trade record survives.</summary>
    public int? OriginalLotId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ImportedAt { get; set; }

    /// <summary>Set the first time a replacement scan is linked and committed. A trade can
    /// receive further replacements after this (see <see cref="InventoryLot.FulfilledTradeId"/>)
    /// — there's no "closed" state, so this is informational only.</summary>
    public DateTime? FirstFulfilledAt { get; set; }
}
