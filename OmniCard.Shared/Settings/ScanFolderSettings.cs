using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Settings;

/// <summary>
/// Persisted configuration for watched scan folders: one folder per game on the server's disk, where a
/// scanner drops images (one subfolder per batch). After <see cref="QuietPeriodSeconds"/> with no new
/// files, a batch is matched in the background and shows up on the web Scan page.
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

    /// <summary>Seconds with no new file before a batch starts matching.</summary>
    public int QuietPeriodSeconds { get; set; } = DefaultQuietPeriodSeconds;

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
