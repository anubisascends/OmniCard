namespace OmniCard.Models;

/// <summary>A user-defined tag, applied to individual <see cref="InventoryLot"/>s (physical
/// copies) via <see cref="LotTag"/>. Name uniqueness is case-insensitive, enforced by a unique
/// index in <c>UnifiedMigrationService.EnsureUnifiedSchema</c> — this app patches schema by hand
/// rather than using EF migrations.</summary>
public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
