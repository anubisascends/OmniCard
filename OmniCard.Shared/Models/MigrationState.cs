namespace OmniCard.Models;

/// <summary>
/// A tiny keyed marker table used to record that a one-time data migration (e.g. the Phase-2a
/// unified-store migration in <c>UnifiedMigrationService</c>) has completed. A row is inserted in
/// the SAME database transaction as the migrated data so the "migration complete" marker is
/// atomic with the data it describes: either both commit, or neither does. This makes the marker
/// safe to use as the authoritative "already migrated?" guard, even across crashes between a data
/// commit and a separate file-flag write.
/// </summary>
public class MigrationState
{
    public string Key { get; set; } = "";
    public DateTime CompletedAt { get; set; }
}
