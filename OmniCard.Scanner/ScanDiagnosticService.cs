using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Scanner;

public class ScanDiagnosticService(IDbContextFactory<CollectionDbContext> dbContextFactory, IDiagnosticExporter diagnosticExporter) : IScanDiagnosticService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public void LogScanCompleted(string sessionId, ulong scanHash, CardMatch? match, MatchDiagnostics? diagnostics, ulong[]? artHashes, OcrMatchResult? ocrResult, FlagReason autoFlagReason)
    {
        var payload = new Dictionary<string, object?>
        {
            ["matchedCardId"] = match?.GameSpecificId,
            ["matchedName"] = match?.Name,
            ["matchedSet"] = match?.SetCode,
            ["matchedNumber"] = match?.CollectorNumber,
            ["confidence"] = match?.Confidence,
            ["decisionPhase"] = diagnostics?.DecisionPhase ?? "NoMatch",
            ["pHashDistance"] = diagnostics?.PHashDistance ?? 0,
            ["artHashDistance"] = diagnostics?.ArtHashDistance,
            ["ocrRecognizedName"] = diagnostics?.OcrRecognizedName ?? ocrResult?.RecognizedName,
            ["ocrNameConfidence"] = diagnostics?.OcrNameConfidence ?? ocrResult?.NameConfidence,
            ["ocrDetectedSets"] = diagnostics?.OcrDetectedSets,
            ["ocrSymbolConfidence"] = ocrResult?.SymbolConfidence,
            ["setFilterActive"] = diagnostics?.SetFilterActive ?? false,
            ["activeSets"] = diagnostics?.ActiveSets,
            ["preferredSets"] = diagnostics?.PreferredSets,
            ["tieZoneCandidates"] = diagnostics?.TieZoneCandidates,
            ["artHashes"] = artHashes,
            ["autoFlagReason"] = autoFlagReason.ToString(),
        };

        LogEvent(sessionId, scanHash, "ScanCompleted", payload);
    }

    public void LogUserFlagged(ulong scanHash, ScannedCard card)
    {
        var sessionId = EnsureScanEventExists(scanHash, card);
        var payload = new Dictionary<string, object?>
        {
            ["currentCardId"] = card.Match?.GameSpecificId,
            ["currentName"] = card.Match?.Name,
            ["currentSet"] = card.Match?.SetCode,
            ["currentConfidence"] = card.Match?.Confidence,
            ["flagReason"] = "Manual",
        };
        LogEvent(sessionId, scanHash, "UserFlagged", payload);
    }

    public void LogUserConfirmed(ulong scanHash, ScannedCard card)
    {
        var sessionId = EnsureScanEventExists(scanHash, card);
        var payload = new Dictionary<string, object?>
        {
            ["confirmedCardId"] = card.Match?.GameSpecificId,
            ["confirmedName"] = card.Match?.Name,
            ["confirmedSet"] = card.Match?.SetCode,
            ["originalConfidence"] = card.Match?.Confidence,
        };
        LogEvent(sessionId, scanHash, "UserConfirmed", payload);
    }

    public void LogUserCorrected(ulong scanHash, ScannedCard card, CardMatch newMatch)
    {
        var sessionId = EnsureScanEventExists(scanHash, card);
        var wasInTieZone = CheckWasInTieZone(scanHash, newMatch.GameSpecificId);
        var payload = new Dictionary<string, object?>
        {
            ["originalCardId"] = card.Match?.GameSpecificId,
            ["originalName"] = card.Match?.Name,
            ["originalSet"] = card.Match?.SetCode,
            ["originalConfidence"] = card.Match?.Confidence,
            ["correctedCardId"] = newMatch.GameSpecificId,
            ["correctedName"] = newMatch.Name,
            ["correctedSet"] = newMatch.SetCode,
            ["correctedNumber"] = newMatch.CollectorNumber,
            ["wasInTieZone"] = wasInTieZone,
        };
        LogEvent(sessionId, scanHash, "UserCorrected", payload);
    }

    public void LogUserUnflagged(ulong scanHash, ScannedCard card, FlagReason previousReason)
    {
        var sessionId = EnsureScanEventExists(scanHash, card);
        var payload = new Dictionary<string, object?>
        {
            ["cardId"] = card.Match?.GameSpecificId,
            ["cardName"] = card.Match?.Name,
            ["previousFlagReason"] = previousReason.ToString(),
        };
        LogEvent(sessionId, scanHash, "UserUnflagged", payload);
    }

    public void ExportDiagnostics(string filePath)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var events = ctx.ScanDiagnosticEvents
            .AsNoTracking()
            .OrderBy(e => e.Timestamp)
            .ToList();

        var output = diagnosticExporter.Render(events);
        File.WriteAllText(filePath, output);
    }

    public void ClearDiagnostics()
    {
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.ScanDiagnosticEvents.RemoveRange(ctx.ScanDiagnosticEvents);
        ctx.SaveChanges();
    }

    public int GetEventCount()
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.ScanDiagnosticEvents.Count();
    }

    private void LogEvent(string sessionId, ulong scanHash, string eventType, Dictionary<string, object?> payload)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.ScanDiagnosticEvents.Add(new ScanDiagnosticEvent
        {
            SessionId = sessionId,
            ScanHash = scanHash,
            EventType = eventType,
            Timestamp = DateTime.UtcNow,
            Payload = JsonSerializer.Serialize(payload, JsonOpts),
        });
        ctx.SaveChanges();
    }

    private string EnsureScanEventExists(ulong scanHash, ScannedCard card)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var existing = ctx.ScanDiagnosticEvents
            .AsNoTracking()
            .Where(e => e.ScanHash == scanHash && e.EventType == "ScanCompleted")
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault();

        if (existing is not null)
            return existing.SessionId;

        // Backfill a minimal ScanCompleted event from the ScannedCard
        var payload = new Dictionary<string, object?>
        {
            ["matchedCardId"] = card.Match?.GameSpecificId,
            ["matchedName"] = card.Match?.Name,
            ["matchedSet"] = card.Match?.SetCode,
            ["matchedNumber"] = card.Match?.CollectorNumber,
            ["confidence"] = card.Match?.Confidence,
            ["decisionPhase"] = "Backfilled",
            ["pHashDistance"] = 0,
            ["autoFlagReason"] = card.FlagReason.ToString(),
            ["tieZoneCandidates"] = Array.Empty<object>(),
        };

        var sessionId = "backfilled";
        LogEvent(sessionId, scanHash, "ScanCompleted", payload);
        return sessionId;
    }

    private string LookupSessionId(ulong scanHash)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var scanEvent = ctx.ScanDiagnosticEvents
            .AsNoTracking()
            .Where(e => e.ScanHash == scanHash && e.EventType == "ScanCompleted")
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault();
        return scanEvent?.SessionId ?? "orphaned";
    }

    private bool CheckWasInTieZone(ulong scanHash, string correctedCardId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var scanEvent = ctx.ScanDiagnosticEvents
            .AsNoTracking()
            .Where(e => e.ScanHash == scanHash && e.EventType == "ScanCompleted")
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault();

        if (scanEvent is null) return false;

        try
        {
            using var payload = JsonDocument.Parse(scanEvent.Payload);
            if (payload.RootElement.TryGetProperty("tieZoneCandidates", out var candidates))
            {
                foreach (var candidate in candidates.EnumerateArray())
                {
                    if (candidate.TryGetProperty("CardId", out var cardId) &&
                        cardId.GetString() == correctedCardId)
                        return true;
                }
            }
        }
        catch { }

        return false;
    }
}
