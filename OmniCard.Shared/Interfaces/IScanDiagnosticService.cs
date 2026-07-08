using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IScanDiagnosticService
{
    void LogScanCompleted(string sessionId, ulong scanHash, CardMatch? match, MatchDiagnostics? diagnostics, ulong[]? artHashes, OcrMatchResult? ocrResult, FlagReason autoFlagReason);
    void LogUserFlagged(ulong scanHash, ScannedCard card);
    void LogUserConfirmed(ulong scanHash, ScannedCard card);
    void LogUserCorrected(ulong scanHash, ScannedCard card, CardMatch newMatch);
    void LogUserUnflagged(ulong scanHash, ScannedCard card, FlagReason previousReason);
    void ExportDiagnostics(string filePath);
    void ClearDiagnostics();
    int GetEventCount();
}
