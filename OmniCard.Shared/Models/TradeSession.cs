namespace OmniCard.Models;

/// <summary>A single trade "action" — potentially many cards going out at once (owned lots and/or
/// off-database card-show pickups), reconciled against a single received haul (one note + one
/// photo). Created by <c>TradeImportService</c> from a web-app <see cref="TradeSessionRecord"/>
/// (schema v2) or synthesised around a legacy single-card <see cref="TradeRecord"/> (schema v1) so
/// every trade has a uniform session. Each outgoing card is a child <see cref="Trade"/> row
/// (<see cref="Trade.TradeSessionId"/>); the session owns the received side.</summary>
public class TradeSession
{
    public int Id { get; set; }

    /// <summary>Ties back to the <c>trades/{SessionRecordId}/</c> folder for photo lookup.</summary>
    public Guid SessionRecordId { get; set; }

    /// <summary>Free-text note describing the trade / the received cards.</summary>
    public string Note { get; set; } = "";

    /// <summary>Absolute path to the photo of the cards received in the trade, if any.</summary>
    public string? ReceivedPhotoPath { get; set; }

    /// <summary>Sum of the outgoing cards' estimated values at finalize time (market price for
    /// owned cards, manual entry for off-database cards).</summary>
    public decimal OutgoingValue { get; set; }

    /// <summary>Manually entered value of the received cards, for fairness/balance context. Null
    /// when the user didn't estimate it.</summary>
    public decimal? ReceivedValue { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ImportedAt { get; set; }

    /// <summary>Set the first time a replacement scan is linked and committed (see
    /// <see cref="InventoryLot.FulfilledTradeSessionId"/>). Informational only — a session can keep
    /// receiving replacements after this; there is no "closed" state.</summary>
    public DateTime? FirstFulfilledAt { get; set; }
}
