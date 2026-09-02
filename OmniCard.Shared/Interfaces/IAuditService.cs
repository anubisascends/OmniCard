using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IAuditService
{
    bool IsAuditActive { get; }
    int? AuditLocationId { get; }
    string? AuditLocationName { get; }
    void StartAudit(int containerId);
    void EndAudit();
    CardMatch? FindScopedMatch(ulong hash, ulong[]? artHashes);
    AuditReport GenerateReport(IEnumerable<ScannedCard> scannedCards);

    /// <summary>One-shot audit of a location against an imported collection (e.g. a Manabox / Mythic
    /// Tools export). Loads the location's expected cards fresh and diffs the imported cards against
    /// them — matched first by card id then by set code + collector number — reporting missing, extra,
    /// and condition/foil discrepancies. Does not enter scan audit mode.</summary>
    AuditReport GenerateFileAuditReport(int containerId, IEnumerable<CollectionCard> importedCards);
}
