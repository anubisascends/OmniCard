using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IScanArchiveService
{
    /// <summary>Zips every image currently under the scans directory, plus a
    /// <c>scan_mapping.json</c> linking each file back to its <see cref="Models.InventoryLot"/>
    /// id, into a timestamped archive. Never throws — failures are reported via the result.</summary>
    Task<ScanArchiveResult> ArchiveCurrentScansAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>Extracts a previously generated archive back into the scans directory and
    /// relinks each image to its lot via <c>scan_mapping.json</c>. Entries whose lot no longer
    /// exists still have their image extracted but are reported as orphaned. Never throws —
    /// failures are reported via the result.</summary>
    Task<ScanRestoreResult> ImportArchiveAsync(string zipPath, IProgress<string>? progress = null, CancellationToken ct = default);
}
