namespace OmniCard.Models;

/// <summary>Permanent record of a single card going out in a trade — one row per outgoing card,
/// grouped under a <see cref="TradeSession"/> (<see cref="TradeSessionId"/>). Created by
/// <c>TradeImportService</c> from a web-app trade record, and outlives the traded-away
/// <see cref="InventoryLot"/> once a replacement scan is committed and the lot is deleted (see
/// <see cref="InventoryLot.FulfilledTradeSessionId"/>).</summary>
public class Trade
{
    public int Id { get; set; }

    /// <summary>The session this outgoing card belongs to. Nullable only for transitional/legacy
    /// rows before the session backfill runs; every row gets a session on import.</summary>
    public int? TradeSessionId { get; set; }

    /// <summary>Ties back to the <c>trades/{TradeRecordId}/</c> folder — matches the owning
    /// session's <see cref="TradeSession.SessionRecordId"/>.</summary>
    public Guid TradeRecordId { get; set; }

    // Snapshot of the traded-away card, copied from the trade record at import time.
    public CardGame Game { get; set; }
    public string CardName { get; set; } = "";
    public string? SetCode { get; set; }
    public string? SetName { get; set; }
    public string? CollectorNumber { get; set; }
    public bool Foil { get; set; }

    /// <summary>True when this outgoing card was never in the collection DB (a card-show pickup
    /// traded away in-hand). Such rows have no <see cref="OriginalLotId"/> and never fulfill.</summary>
    public bool IsOffDatabase { get; set; }

    /// <summary>Photo of an off-database outgoing card (there is no lot / scan image to fall back
    /// on). Null for owned cards.</summary>
    public string? OffDbPhotoPath { get; set; }

    /// <summary>Value of this outgoing card at finalize (owned = market price; off-database =
    /// user's manual estimate, if any). Summed into <see cref="TradeSession.OutgoingValue"/>.</summary>
    public decimal? EstimatedValue { get; set; }

    /// <summary>Legacy per-card note/photo carried on pre-session (schema v1) rows — the received
    /// note/photo now live on <see cref="TradeSession"/>. Kept for reading old data only.</summary>
    public string Note { get; set; } = "";
    public string? PhotoPath { get; set; }

    /// <summary>The owned lot that was traded away. Cleared (set null) once a linked replacement is
    /// committed and the lot is deleted — the trade itself is kept regardless, so this only tracks
    /// whether the removal side has happened yet. Null for off-database cards.</summary>
    public int? OriginalLotId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ImportedAt { get; set; }

    /// <summary>Set the first time a replacement scan is linked and committed. Session-level
    /// fulfillment also stamps <see cref="TradeSession.FirstFulfilledAt"/>; this per-card marker is
    /// retained for legacy rows.</summary>
    public DateTime? FirstFulfilledAt { get; set; }
}
