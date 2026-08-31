using OmniCard.Models;

namespace OmniCard.Interfaces;

/// <summary>
/// Serializes a scanning session (the pending <see cref="ScannedCard"/> queue plus its metadata) to a
/// self-contained <c>.ocss</c> file — a zip of the scan images and a JSON manifest — and reads it back,
/// rehydrating each card's matched catalog object and per-card location override. Also maintains a
/// single crash-recovery autosave so an unexpected exit doesn't lose an in-progress session.
/// </summary>
public interface IScanSessionService
{
    /// <summary>The <c>.ocss</c> file extension (including the dot) and a file-dialog filter.</summary>
    string FileExtension { get; }
    string FileDialogFilter { get; }

    /// <summary>Writes the session and its cards to <paramref name="filePath"/> (a <c>.ocss</c> zip).</summary>
    Task SaveAsync(ScanSession session, IReadOnlyList<ScannedCard> cards, string filePath, CancellationToken ct = default);

    /// <summary>Opens a <c>.ocss</c> file: extracts its scan images into the temp-scans folder and
    /// rebuilds the card queue, re-resolving each match's catalog object and override container from
    /// the live data so the reopened cards commit identically.</summary>
    Task<ScanSessionOpenResult> OpenAsync(string filePath, CancellationToken ct = default);

    /// <summary>Writes the current session to the fixed crash-recovery path (overwriting any previous
    /// autosave). Cheap enough to call after each scan/edit; failures are swallowed and logged.</summary>
    Task AutosaveAsync(ScanSession session, IReadOnlyList<ScannedCard> cards, CancellationToken ct = default);

    /// <summary>True if a recovery autosave from a previous run is present. <paramref name="savedUtc"/>
    /// is when it was written.</summary>
    bool TryGetRecoverable(out DateTime savedUtc);

    /// <summary>Loads the recovery autosave (see <see cref="OpenAsync"/> semantics).</summary>
    Task<ScanSessionOpenResult> RecoverAsync(CancellationToken ct = default);

    /// <summary>Deletes the recovery autosave — call once a session is committed or safely saved.</summary>
    void ClearRecovery();
}

/// <summary>The result of opening/recovering a session: its metadata and the rebuilt card queue.</summary>
public sealed record ScanSessionOpenResult(ScanSession Session, List<ScannedCard> Cards);
