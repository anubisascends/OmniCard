namespace OmniCard.Models;

/// <summary>File-based handoff record for a multi-card trade "session" made from the web companion
/// app (schema v2). Written by the web app (never touches the read-only SQLite DBs), picked up and
/// applied by the desktop app on next launch. One folder per session under
/// <see cref="OmniCard.Interfaces.IDataPathService.TradesDirectory"/>:
/// <c>{SessionId}/trade.json</c> alongside the received photo and any off-database card photos.
/// <para>The folder is created as a <c>draft</c> when the user starts a trade and appended to as
/// cards are added; the desktop importer only applies records once <see cref="Status"/> is
/// <c>final</c>. <see cref="SchemaVersion"/> distinguishes this from the legacy single-card
/// <see cref="TradeRecord"/> (which has no version field, i.e. version 1).</para></summary>
public class TradeSessionRecord
{
    /// <summary>2 for this session-based schema. A missing/1 value means the legacy
    /// <see cref="TradeRecord"/> shape.</summary>
    public int SchemaVersion { get; set; } = 2;

    /// <summary><c>"draft"</c> while the user is still building the trade on the web app;
    /// <c>"final"</c> once finalized. The desktop importer skips drafts.</summary>
    public string Status { get; set; } = "draft";

    public Guid SessionId { get; init; } = Guid.NewGuid();

    public string Note { get; set; } = "";

    /// <summary>File name of the received-cards photo within this folder (e.g. "received.jpg").</summary>
    public string? ReceivedPhotoFileName { get; set; }

    /// <summary>Manually entered value of the received cards, for fairness context.</summary>
    public decimal? ReceivedValue { get; set; }

    public List<TradeOutgoingItem> OutgoingItems { get; set; } = [];

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Null until the desktop app applies this session to the collection.</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Set (instead of applying) if application fails — still marks the record processed
    /// so it doesn't retry forever.</summary>
    public string? ProcessingError { get; set; }
}

/// <summary>One outgoing card within a <see cref="TradeSessionRecord"/> — either an owned lot
/// (identified by <see cref="LotId"/>, snapshot filled from the collection) or an off-database
/// card-show pickup (<see cref="IsOffDatabase"/>, captured via <see cref="PhotoFileName"/> plus an
/// optional name/value).</summary>
public class TradeOutgoingItem
{
    /// <summary>The owned lot being traded away, or null for an off-database card.</summary>
    public int? LotId { get; set; }

    public bool IsOffDatabase { get; set; }

    public CardGame Game { get; set; }
    public string CardName { get; set; } = "";
    public string? SetCode { get; set; }
    public string? SetName { get; set; }
    public string? CollectorNumber { get; set; }
    public bool Foil { get; set; }

    /// <summary>Market price (owned) or manual estimate (off-database), if known.</summary>
    public decimal? EstimatedValue { get; set; }

    /// <summary>File name of this card's photo within the session folder — set for off-database
    /// cards (e.g. "outgoing-1.jpg"), null for owned cards (the desktop uses the lot's scan).</summary>
    public string? PhotoFileName { get; set; }
}
