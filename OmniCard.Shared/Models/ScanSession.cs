using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniCard.Models;

/// <summary>
/// A save/resumable scanning session. The pending scanned cards themselves live in
/// <see cref="OmniCard.Interfaces.ICardService.ScannedCards"/>; this holds the session's identity,
/// where it was last saved, and whether it has changes not yet written to disk. A session is created
/// via "New Scan Session", persisted to a <c>.ocss</c> file, and closed when its cards are committed.
/// </summary>
public partial class ScanSession : ObservableObject
{
    /// <summary>Stable id, used to name the working temp folder and detect the same session on recovery.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Display name — the saved file's name without extension, or "Untitled" until first saved.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = "Untitled";

    /// <summary>Full path of the file this session was last saved to / opened from; null if never saved.</summary>
    [ObservableProperty]
    public partial string? FilePath { get; set; }

    /// <summary>True when the in-memory queue has changes not yet written to <see cref="FilePath"/>.
    /// Drives the save-prompt guard and the title-bar dirty marker.</summary>
    [ObservableProperty]
    public partial bool HasUnsavedChanges { get; set; }

    public bool HasBeenSaved => FilePath is not null;
}
