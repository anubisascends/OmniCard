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
}
