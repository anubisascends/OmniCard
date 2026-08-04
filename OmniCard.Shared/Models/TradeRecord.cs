namespace OmniCard.Models;

/// <summary>File-based handoff record for a trade made from the web companion app: written by
/// the web app (never touches the SQLite DBs — see <c>OmniCard.Web</c>'s read-only invariant),
/// picked up and applied by the desktop app on next launch. One JSON file per trade under
/// <see cref="OmniCard.Interfaces.IDataPathService.TradesDirectory"/>, in its own
/// <c>{TradeId}/trade.json</c> folder alongside the photo. The folder is never deleted, so it
/// stays around for the user to review later; <see cref="ProcessedAt"/> is the idempotency
/// marker preventing re-application on a later launch.</summary>
public class TradeRecord
{
    public Guid TradeId { get; init; } = Guid.NewGuid();

    /// <summary>The <see cref="InventoryLot"/> being traded away.</summary>
    public int LotId { get; init; }

    // Snapshot of the traded-away card's identity, captured on the web app at trade-creation
    // time (from the CollectionCard it already has loaded) — survives the lot itself being
    // deleted once a replacement is scanned in and committed, so trade history stays reviewable.
    public CardGame Game { get; init; }
    public string CardName { get; init; } = "";
    public string? SetCode { get; init; }
    public string? SetName { get; init; }
    public string? CollectorNumber { get; init; }
    public bool Foil { get; init; }

    public string Note { get; init; } = "";

    /// <summary>File name of the trade photo within the same folder as this record.</summary>
    public string PhotoFileName { get; init; } = "";

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Null until the desktop app applies this trade to the collection.</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Set instead of applying the trade when <see cref="LotId"/> no longer exists
    /// (e.g. deleted before the desktop caught up) — still marks the record processed so it
    /// doesn't retry forever.</summary>
    public string? ProcessingError { get; set; }
}
