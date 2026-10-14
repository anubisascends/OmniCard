using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Scanning;

/// <summary>Lifecycle of a <see cref="ScanBatch"/>. Collecting (files still in the watched folder,
/// untouched) → Starting (Run pressed, or the quiet period elapsed with auto-processing on) → Matching
/// (files taken in and moved to <c>_processed</c>) → (no pending items) Ready → Committed/Discarded.
/// Files that land after a batch has started form a new batch.</summary>
public enum ScanBatchStatus { Collecting, Starting, Matching, Ready, Committed, Discarded }

/// <summary>Whether a batch item has been through the matcher yet.</summary>
public enum ScanBatchItemStatus { Pending, Matched, Error }

/// <summary>Whether a batch item is still under review, already written to the collection, or removed.
/// <see cref="Duplicate"/> marks a byte-for-byte copy of an image already picked up (scanner software
/// that saved the same files twice): it's set aside at pickup, never matched or committed, and keeps
/// no image of its own.</summary>
public enum ScanBatchItemState { Open, Committed, Removed, Duplicate }

/// <summary>
/// A group of card images picked up from a watched scan folder (one per game; each subfolder is a
/// batch) and matched in the background. Users claim a batch to review and commit it from the web
/// Scan page. The match settings are a snapshot of the folder configuration at creation time, so later
/// configuration edits don't change a batch already in flight.
/// </summary>
public class ScanBatch
{
    public int Id { get; set; }
    public CardGame Game { get; set; }

    /// <summary>Display name: the folder key, suffixed " (n)" when an earlier closed batch used it.</summary>
    public string Name { get; set; } = "";

    /// <summary>The source subfolder name, or empty for files dropped in the game folder's root (older
    /// root batches hold the date). Files under this key belong to the batch until it starts.</summary>
    public string FolderKey { get; set; } = "";

    public ScanBatchStatus Status { get; set; }
    public DateTime CreatedUtc { get; set; }

    /// <summary>When the batch's files last changed (added, renamed, resized) — the quiet-period anchor.</summary>
    public DateTime LastFileUtc { get; set; }

    /// <summary>Image files still sitting in the watched folder while the batch is Collecting; 0 once
    /// they're taken in.</summary>
    public int WaitingFiles { get; set; }

    public DateTime? ReadyUtc { get; set; }

    /// <summary>When the batch was committed or discarded — the retention anchor.</summary>
    public DateTime? ClosedUtc { get; set; }

    // Match settings (snapshot of the folder configuration)
    public bool IsFoil { get; set; }
    public string? Condition { get; set; }
    public string? Language { get; set; }
    /// <summary>Comma-separated "Sets (art fallback)" codes; null/empty = no set constraint.</summary>
    public string? SetCodes { get; set; }
    public int? DefaultContainerId { get; set; }

    // Claim (one reviewer at a time)
    public int? ClaimedByUserId { get; set; }
    public string? ClaimedByName { get; set; }
    public DateTime? ClaimedUtc { get; set; }

    public List<ScanBatchItem> Items { get; set; } = [];

    /// <summary>True while the batch can still receive files and be reviewed.</summary>
    public bool IsOpen => Status is not (ScanBatchStatus.Committed or ScanBatchStatus.Discarded);

    public IReadOnlyList<string> SetCodeList =>
        string.IsNullOrWhiteSpace(SetCodes)
            ? []
            : SetCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>One scanned image in a <see cref="ScanBatch"/>, with its match and the reviewer's
/// per-copy properties (mirrors the SPA's scan item).</summary>
public class ScanBatchItem
{
    public int Id { get; set; }
    public int ScanBatchId { get; set; }

    /// <summary>Ingest order — the stable display order.</summary>
    public int Sequence { get; set; }

    public string OriginalFileName { get; set; } = "";

    /// <summary>File name inside the batch directory (<c>{Id}{ext}</c>).</summary>
    public string StoredFileName { get; set; } = "";

    /// <summary>SHA-256 of the image file (lowercase hex) — how exact duplicates are recognized. Null for
    /// items picked up before duplicate detection existed.</summary>
    public string? ContentHash { get; set; }

    /// <summary>For a <see cref="ScanBatchItemState.Duplicate"/>: the earlier item with the same bytes
    /// (possibly in another batch of the same game).</summary>
    public int? DuplicateOfItemId { get; set; }

    /// <summary>Where the original file was moved under the watched folder's <c>_processed</c>
    /// subfolder — deleted when the batch closes if that option is on. Null until the move succeeds.</summary>
    public string? ProcessedPath { get; set; }

    /// <summary>A browser-renderable JPEG preview for TIFF scans; null when the stored file is fine.</summary>
    public string? PreviewFileName { get; set; }

    public ScanBatchItemStatus Status { get; set; }
    public ScanBatchItemState State { get; set; }

    /// <summary>Serialized match result (the web layer's ScanMatchDto).</summary>
    public string? MatchJson { get; set; }

    /// <summary>Serialized user correction (the web layer's ScanSearchResultDto); wins over the match.</summary>
    public string? OverrideJson { get; set; }

    public string? Error { get; set; }

    // Per-copy properties
    public bool Include { get; set; }
    public bool Verified { get; set; }
    public string Condition { get; set; } = "NM";
    public string? Language { get; set; }
    public bool IsFoil { get; set; }
    public string? FoilType { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal? PurchasePrice { get; set; }
    /// <summary>Serialized string array of tags.</summary>
    public string? TagsJson { get; set; }
    public string? Note { get; set; }
}
