using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Settings;

/// <summary>
/// Persisted configuration for watched scan folders: one folder per game on the server's disk, where a
/// scanner drops images (one subfolder per batch). A batch's files are left alone in the folder until
/// it starts — when someone presses Run, or (with <see cref="AutoProcess"/>) once they've gone
/// <see cref="QuietPeriodSeconds"/> without changing. Then they're taken in, moved to <c>_processed</c>
/// and matched in the background, and the batch shows up for review on the web Scan page.
/// </summary>
public sealed class ScanFolderSettings
{
    public const int DefaultQuietPeriodSeconds = 90;
    public const int MinQuietPeriodSeconds = 10;
    public const int MaxQuietPeriodSeconds = 3600;
    public const int DefaultRetentionDays = 14;
    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 365;

    /// <summary>Master switch for folder watching (default off). Batches already picked up still finish
    /// matching when this is off.</summary>
    public bool Enabled { get; set; }

    /// <summary>Start a batch on its own once its quiet period passes (default on). When off, a batch
    /// waits — its files untouched in the folder — until someone presses Run on the Scan page.</summary>
    public bool AutoProcess { get; set; } = true;

    /// <summary>Seconds a batch's files must go unchanged before it starts on its own. Each batch
    /// counts down separately.</summary>
    public int QuietPeriodSeconds { get; set; } = DefaultQuietPeriodSeconds;

    /// <summary>Delete a batch's original files from <c>_processed</c> once it's committed or discarded
    /// (default off: they're kept).</summary>
    public bool DeleteOriginalsOnClose { get; set; }

    /// <summary>Days a committed/discarded batch's stored images are kept before they're purged.</summary>
    public int RetentionDays { get; set; } = DefaultRetentionDays;

    public List<ScanFolderConfig> Folders { get; set; } = [];
}

/// <summary>One game's watched folder and the match settings its new batches start with.</summary>
public sealed class ScanFolderConfig
{
    public CardGame Game { get; set; }
    public string Path { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool IsFoil { get; set; }
    public string Condition { get; set; } = "NM";
    /// <summary>The cards' language (CardLanguages code), or null for auto-detect.</summary>
    public string? Language { get; set; }
    /// <summary>"Sets (art fallback)" constraint; empty = all sets.</summary>
    public List<string> SetCodes { get; set; } = [];
    /// <summary>Storage location pre-selected when a batch is committed.</summary>
    public int? DefaultContainerId { get; set; }
}
