using OmniCard.Models;
using OmniCard.Audit;

namespace OmniCard.Tests.Services;

public class DiagnosticExportTests
{
    [Fact]
    public void EmptyExport_HasHeaderAndZeroCounts()
    {
        var exporter = new DiagnosticExporter();
        var output = exporter.Render([]);

        Assert.Contains("=== SCAN DIAGNOSTIC EXPORT ===", output);
        Assert.Contains("Total Sessions: 0", output);
        Assert.Contains("Total Events: 0", output);
        Assert.Contains("Total Scans: 0", output);
    }

    [Fact]
    public void SingleScan_NoUserAction_ShowsOutcome()
    {
        var events = new List<ScanDiagnosticEvent>
        {
            new()
            {
                SessionId = "session-1",
                ScanHash = 0xAABBCCDD,
                EventType = "ScanCompleted",
                Timestamp = new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc),
                Payload = """{"matchedName":"Lightning Bolt","matchedSet":"m21","matchedNumber":"199","confidence":87.5,"decisionPhase":"PHashConfident","pHashDistance":3,"autoFlagReason":"None","tieZoneCandidates":[{"cardId":"abc","name":"Lightning Bolt","set":"m21","number":"199","pHashDist":3,"finalScore":-2,"selected":true}]}""",
            }
        };
        var exporter = new DiagnosticExporter();
        var output = exporter.Render(events);

        Assert.Contains("SESSION: session-1", output);
        Assert.Contains("scan_hash=0x00000000AABBCCDD", output);
        Assert.Contains("Decision: PHashConfident", output);
        Assert.Contains("Lightning Bolt | m21 #199", output);
        Assert.Contains("Confidence: 87.5%", output);
        Assert.Contains("[SELECTED]", output);
        Assert.Contains("OUTCOME: No user action", output);
        Assert.Contains("Auto-Accepted (no user action): 1", output);
    }

    [Fact]
    public void CorrectedScan_ShowsUserActions()
    {
        var events = new List<ScanDiagnosticEvent>
        {
            new()
            {
                SessionId = "s1", ScanHash = 0x1111, EventType = "ScanCompleted",
                Timestamp = new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc),
                Payload = """{"matchedName":"Wrong Card","matchedSet":"m21","confidence":90,"decisionPhase":"PHashConfident","pHashDistance":2,"autoFlagReason":"None","tieZoneCandidates":[]}""",
            },
            new()
            {
                SessionId = "s1", ScanHash = 0x1111, EventType = "UserFlagged",
                Timestamp = new DateTime(2026, 7, 6, 10, 5, 0, DateTimeKind.Utc),
                Payload = """{"currentName":"Wrong Card","flagReason":"Manual"}""",
            },
            new()
            {
                SessionId = "s1", ScanHash = 0x1111, EventType = "UserCorrected",
                Timestamp = new DateTime(2026, 7, 6, 10, 5, 30, DateTimeKind.Utc),
                Payload = """{"originalName":"Wrong Card","originalSet":"m21","originalConfidence":90,"correctedName":"Right Card","correctedSet":"2xm","correctedNumber":"42","wasInTieZone":false}""",
            },
        };
        var exporter = new DiagnosticExporter();
        var output = exporter.Render(events);

        Assert.Contains("USER FLAGGED", output);
        Assert.Contains("USER CORRECTED", output);
        Assert.Contains("Was: Wrong Card", output);
        Assert.Contains("Now: Right Card", output);
        Assert.Contains("Correct card was in tie zone: NO", output);
        Assert.Contains("OUTCOME: Corrected", output);
        Assert.Contains("User Corrected: 1", output);
    }

    [Fact]
    public void SummaryStatistics_CountsCorrectly()
    {
        var events = new List<ScanDiagnosticEvent>
        {
            MakeScanEvent("s1", 0x1111, "Card A", 90, "PHashConfident"),
            MakeScanEvent("s1", 0x2222, "Card B", 45, "OcrAssisted"),
            new() { SessionId = "s1", ScanHash = 0x2222, EventType = "UserConfirmed", Timestamp = DateTime.UtcNow, Payload = """{"confirmedName":"Card B"}""" },
            MakeScanEvent("s1", 0x3333, "Card C", 85, "PHashConfident"),
            new() { SessionId = "s1", ScanHash = 0x3333, EventType = "UserCorrected", Timestamp = DateTime.UtcNow, Payload = """{"originalConfidence":85,"correctedName":"Card D","wasInTieZone":true}""" },
        };
        var exporter = new DiagnosticExporter();
        var output = exporter.Render(events);

        Assert.Contains("Total Scans: 3", output);
        Assert.Contains("Auto-Accepted (no user action): 1", output);
        Assert.Contains("User Confirmed: 1", output);
        Assert.Contains("User Corrected: 1", output);
        Assert.Contains("PHashConfident: 2", output);
        Assert.Contains("OcrAssisted: 1", output);
    }

    private static ScanDiagnosticEvent MakeScanEvent(string session, ulong hash, string name, double confidence, string phase) =>
        new()
        {
            SessionId = session,
            ScanHash = hash,
            EventType = "ScanCompleted",
            Timestamp = DateTime.UtcNow,
            Payload = $$"""{"matchedName":"{{name}}","confidence":{{confidence}},"decisionPhase":"{{phase}}","pHashDistance":3,"autoFlagReason":"None","tieZoneCandidates":[]}""",
        };
}
