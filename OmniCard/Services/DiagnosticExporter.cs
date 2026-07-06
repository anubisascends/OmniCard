using System.Reflection;
using System.Text;
using System.Text.Json;
using OmniCard.Models;

namespace OmniCard.Services;

public class DiagnosticExporter(List<ScanDiagnosticEvent> events)
{
    public string Render()
    {
        var sb = new StringBuilder();
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

        var sessions = events.GroupBy(e => e.SessionId).OrderBy(g => g.Min(e => e.Timestamp)).ToList();
        var scanEvents = events.Where(e => e.EventType == "ScanCompleted").ToList();

        // Header
        sb.AppendLine("=== SCAN DIAGNOSTIC EXPORT ===");
        sb.AppendLine($"App Version: {version}");
        sb.AppendLine($"Exported: {DateTime.UtcNow:O}");
        sb.AppendLine($"Total Sessions: {sessions.Count}");
        sb.AppendLine($"Total Events: {events.Count}");
        sb.AppendLine($"Total Scans: {scanEvents.Count}");
        sb.AppendLine();

        // Track stats
        int totalScans = 0, autoAccepted = 0, confirmed = 0, corrected = 0, correctedInTieZone = 0, correctedNotInTieZone = 0, flaggedThenUnflagged = 0;
        double confidenceSum = 0, correctedConfidenceSum = 0;
        var phaseBreakdown = new Dictionary<string, int>();
        var highConfMismatches = new List<string>();

        foreach (var session in sessions)
        {
            var sessionEvents = session.OrderBy(e => e.Timestamp).ToList();
            var scansInSession = sessionEvents.Where(e => e.EventType == "ScanCompleted").ToList();
            var flagsInSession = sessionEvents.Count(e => e.EventType == "UserFlagged");
            var correctionsInSession = sessionEvents.Count(e => e.EventType == "UserCorrected");

            sb.AppendLine("================================================================");
            sb.AppendLine($"SESSION: {session.Key}");
            sb.AppendLine($"Started: {sessionEvents.First().Timestamp:O}");
            sb.AppendLine($"Cards Scanned: {scansInSession.Count}");
            sb.AppendLine($"Flags Raised: {flagsInSession}");
            sb.AppendLine($"Corrections Made: {correctionsInSession}");
            sb.AppendLine("================================================================");
            sb.AppendLine();

            // Group events by scan hash
            var cardGroups = sessionEvents.GroupBy(e => e.ScanHash).ToList();

            foreach (var cardGroup in cardGroups)
            {
                var cardEvents = cardGroup.OrderBy(e => e.Timestamp).ToList();
                var scanEvt = cardEvents.FirstOrDefault(e => e.EventType == "ScanCompleted");
                if (scanEvt is null) continue;

                totalScans++;
                var scanPayload = JsonDocument.Parse(scanEvt.Payload);

                sb.AppendLine($"--- CARD: scan_hash=0x{cardGroup.Key:X16} ---");
                RenderScanResult(sb, scanEvt, scanPayload);

                var confidence = GetDouble(scanPayload, "confidence");
                if (confidence.HasValue) confidenceSum += confidence.Value;

                var phase = GetString(scanPayload, "decisionPhase") ?? "Unknown";
                phaseBreakdown[phase] = phaseBreakdown.GetValueOrDefault(phase) + 1;

                // Render user actions
                var userActions = cardEvents.Where(e => e.EventType != "ScanCompleted").ToList();
                foreach (var action in userActions)
                {
                    var actionPayload = JsonDocument.Parse(action.Payload);
                    RenderUserAction(sb, action, actionPayload);
                }

                // Determine outcome
                var hasConfirm = userActions.Any(e => e.EventType == "UserConfirmed");
                var hasCorrect = userActions.Any(e => e.EventType == "UserCorrected");
                var hasFlagOnly = userActions.Any(e => e.EventType == "UserFlagged") && !hasCorrect && !hasConfirm;
                var hasUnflag = userActions.Any(e => e.EventType == "UserUnflagged");

                string outcome;
                if (hasCorrect)
                {
                    outcome = "Corrected";
                    corrected++;
                    if (confidence.HasValue) correctedConfidenceSum += confidence.Value;

                    var correctEvt = userActions.First(e => e.EventType == "UserCorrected");
                    var correctPayload = JsonDocument.Parse(correctEvt.Payload);
                    var wasInTieZone = GetBool(correctPayload, "wasInTieZone");
                    if (wasInTieZone == true) correctedInTieZone++;
                    else correctedNotInTieZone++;

                    if (confidence is >= 80)
                    {
                        var origName = GetString(scanPayload, "matchedName");
                        var origSet = GetString(scanPayload, "matchedSet");
                        var origNum = GetString(scanPayload, "matchedNumber");
                        var corrName = GetString(correctPayload, "correctedName");
                        var corrSet = GetString(correctPayload, "correctedSet");
                        var corrNum = GetString(correctPayload, "correctedNumber");
                        var tieStr = wasInTieZone == true ? "IN tie zone" : "NOT in tie zone";
                        highConfMismatches.Add($"  {origName} | {origSet} #{origNum} -> {corrName} | {corrSet} #{corrNum} | Was {confidence:F1}% confident | {tieStr}");
                    }
                }
                else if (hasConfirm) { outcome = "Confirmed"; confirmed++; }
                else if (hasFlagOnly && hasUnflag) { outcome = "Flagged then unflagged"; flaggedThenUnflagged++; }
                else { outcome = "No user action"; autoAccepted++; }

                sb.AppendLine($"  OUTCOME: {outcome}");
                sb.AppendLine();
            }
        }

        // Summary
        sb.AppendLine("=== SUMMARY STATISTICS ===");
        sb.AppendLine($"Total Scans: {totalScans}");
        sb.AppendLine($"Auto-Accepted (no user action): {autoAccepted}");
        sb.AppendLine($"User Confirmed: {confirmed}");
        sb.AppendLine($"User Corrected: {corrected}");
        if (corrected > 0)
        {
            var tieZonePct = corrected > 0 ? correctedInTieZone * 100.0 / corrected : 0;
            var notTieZonePct = corrected > 0 ? correctedNotInTieZone * 100.0 / corrected : 0;
            sb.AppendLine($"  Correct card was in tie zone: {correctedInTieZone} ({tieZonePct:F0}%)");
            sb.AppendLine($"  Correct card was NOT in tie zone: {correctedNotInTieZone} ({notTieZonePct:F0}%)");
        }
        sb.AppendLine($"User Flagged then Unflagged: {flaggedThenUnflagged}");
        sb.AppendLine($"Average Confidence (all scans): {(totalScans > 0 ? confidenceSum / totalScans : 0):F1}%");
        sb.AppendLine($"Average Confidence (corrected scans): {(corrected > 0 ? correctedConfidenceSum / corrected : 0):F1}%");
        sb.AppendLine("Decision Phase Breakdown:");
        foreach (var (phase, count) in phaseBreakdown.OrderBy(p => p.Key))
            sb.AppendLine($"  {phase}: {count}");
        sb.AppendLine($"High-Confidence Mismatches (>=80% confidence, user corrected): {highConfMismatches.Count}");
        foreach (var line in highConfMismatches)
            sb.AppendLine(line);

        return sb.ToString();
    }

    private static void RenderScanResult(StringBuilder sb, ScanDiagnosticEvent evt, JsonDocument payload)
    {
        var phase = GetString(payload, "decisionPhase") ?? "Unknown";
        var name = GetString(payload, "matchedName") ?? "(no match)";
        var set = GetString(payload, "matchedSet") ?? "???";
        var number = GetString(payload, "matchedNumber") ?? "?";
        var confidence = GetDouble(payload, "confidence");
        var pHashDist = GetInt(payload, "pHashDistance");
        var artHashDist = GetInt(payload, "artHashDistance");
        var ocrName = GetString(payload, "ocrRecognizedName");
        var ocrConf = GetDouble(payload, "ocrNameConfidence");
        var setFilterActive = GetBool(payload, "setFilterActive");
        var autoFlag = GetString(payload, "autoFlagReason") ?? "None";

        sb.AppendLine($"SCAN RESULT [{evt.Timestamp:O}]");
        sb.AppendLine($"  Decision: {phase}");
        sb.AppendLine($"  Match: {name} | {set} #{number} | Confidence: {confidence?.ToString("F1") ?? "N/A"}%");
        sb.AppendLine($"  pHash Distance: {pHashDist?.ToString() ?? "N/A"} | Art Hash Distance: {artHashDist?.ToString() ?? "N/A"}");

        if (ocrName is not null)
            sb.AppendLine($"  OCR Name: \"{ocrName}\" (confidence: {ocrConf?.ToString("F2") ?? "N/A"})");

        // OCR sets
        if (payload.RootElement.TryGetProperty("ocrDetectedSets", out var ocrSets) && ocrSets.ValueKind == JsonValueKind.Array)
        {
            var setStrs = new List<string>();
            foreach (var s in ocrSets.EnumerateArray())
            {
                var sc = s.TryGetProperty("setCode", out var scv) ? scv.GetString() :
                         s.TryGetProperty("set", out var sv) ? sv.GetString() : null;
                var conf = s.TryGetProperty("confidence", out var cv) ? cv.GetDouble().ToString("F2") : "?";
                if (sc is not null) setStrs.Add($"{sc} ({conf})");
            }
            if (setStrs.Count > 0)
                sb.AppendLine($"  OCR Sets Detected: {string.Join(", ", setStrs)}");
        }

        sb.AppendLine($"  Set Filter: {(setFilterActive == true ? "ON" : "OFF")}");

        if (payload.RootElement.TryGetProperty("activeSets", out var activeSets) && activeSets.ValueKind == JsonValueKind.Array)
        {
            var sets = activeSets.EnumerateArray().Select(s => s.GetString()).Where(s => s is not null);
            sb.AppendLine($"    Active Sets: {string.Join(", ", sets)}");
        }
        if (payload.RootElement.TryGetProperty("preferredSets", out var prefSets) && prefSets.ValueKind == JsonValueKind.Array)
        {
            var sets = prefSets.EnumerateArray().Select(s => s.GetString()).Where(s => s is not null);
            sb.AppendLine($"    Preferred: {string.Join(", ", sets)}");
        }

        sb.AppendLine($"  Auto-Flag: {autoFlag}");

        // Tie zone candidates
        if (payload.RootElement.TryGetProperty("tieZoneCandidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
        {
            var candidateList = candidates.EnumerateArray().ToList();
            sb.AppendLine($"  Tie Zone ({candidateList.Count} candidates):");
            foreach (var c in candidateList)
            {
                var cName = c.TryGetProperty("name", out var cn) ? cn.GetString() :
                            c.TryGetProperty("Name", out var cN) ? cN.GetString() : "?";
                var cSet = c.TryGetProperty("set", out var cs) ? cs.GetString() :
                           c.TryGetProperty("setCode", out var csc) ? csc.GetString() :
                           c.TryGetProperty("SetCode", out var cSC) ? cSC.GetString() : "?";
                var cNum = c.TryGetProperty("number", out var cnu) ? cnu.GetString() :
                           c.TryGetProperty("collectorNumber", out var ccn) ? ccn.GetString() :
                           c.TryGetProperty("CollectorNumber", out var cCN) ? cCN.GetString() : "?";
                var cPHash = c.TryGetProperty("pHashDist", out var cp) ? cp.GetInt32().ToString() :
                             c.TryGetProperty("pHashDistance", out var cpd) ? cpd.GetInt32().ToString() :
                             c.TryGetProperty("PHashDistance", out var cPD) ? cPD.GetInt32().ToString() : "?";
                var cArt = c.TryGetProperty("artHashDist", out var ca) ? ca.GetInt32().ToString() :
                           c.TryGetProperty("artHashDistance", out var cad) ? cad.GetInt32().ToString() :
                           c.TryGetProperty("ArtHashDistance", out var cAD) ? cAD.GetInt32().ToString() : "N/A";
                var cBonus = c.TryGetProperty("setBonus", out var cb) ? cb.GetInt32().ToString() :
                             c.TryGetProperty("SetBonus", out var cB) ? cB.GetInt32().ToString() : "0";
                var cScore = c.TryGetProperty("finalScore", out var cf) ? cf.GetInt32().ToString() :
                             c.TryGetProperty("FinalScore", out var cF) ? cF.GetInt32().ToString() : "?";
                var selected = (c.TryGetProperty("selected", out var sel) || c.TryGetProperty("Selected", out sel)) && sel.GetBoolean();

                var prefix = selected ? "    > [SELECTED] " : "      ";
                sb.AppendLine($"{prefix}{cName} | {cSet} #{cNum} | pHash: {cPHash}, artHash: {cArt}, setBonus: {cBonus}, finalScore: {cScore}");
            }
        }
    }

    private static void RenderUserAction(StringBuilder sb, ScanDiagnosticEvent evt, JsonDocument payload)
    {
        switch (evt.EventType)
        {
            case "UserFlagged":
                var flagReason = GetString(payload, "flagReason") ?? "Manual";
                var flagName = GetString(payload, "currentName") ?? "?";
                var flagConf = GetDouble(payload, "currentConfidence");
                sb.AppendLine($"  USER FLAGGED [{evt.Timestamp:O}]");
                sb.AppendLine($"    Reason: {flagReason} | Card at time: {flagName} | Confidence: {flagConf?.ToString("F1") ?? "N/A"}%");
                break;

            case "UserConfirmed":
                var confName = GetString(payload, "confirmedName") ?? "?";
                var confSet = GetString(payload, "confirmedSet") ?? "?";
                sb.AppendLine($"  USER CONFIRMED [{evt.Timestamp:O}]");
                sb.AppendLine($"    Confirmed: {confName} | {confSet}");
                break;

            case "UserCorrected":
                var origName = GetString(payload, "originalName") ?? "?";
                var origSet = GetString(payload, "originalSet") ?? "?";
                var origConf = GetDouble(payload, "originalConfidence");
                var corrName = GetString(payload, "correctedName") ?? "?";
                var corrSet = GetString(payload, "correctedSet") ?? "?";
                var corrNum = GetString(payload, "correctedNumber") ?? "?";
                var wasInTieZone = GetBool(payload, "wasInTieZone");
                sb.AppendLine($"  USER CORRECTED [{evt.Timestamp:O}]");
                sb.AppendLine($"    Was: {origName} | {origSet} ({origConf?.ToString("F1") ?? "N/A"}%)");
                sb.AppendLine($"    Now: {corrName} | {corrSet} #{corrNum}");
                sb.AppendLine($"    Correct card was in tie zone: {(wasInTieZone == true ? "YES" : "NO")}");
                break;

            case "UserUnflagged":
                var prevReason = GetString(payload, "previousFlagReason") ?? "?";
                sb.AppendLine($"  USER UNFLAGGED [{evt.Timestamp:O}]");
                sb.AppendLine($"    Previous reason: {prevReason}");
                break;
        }
    }

    private static string? GetString(JsonDocument doc, string prop) =>
        doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? GetDouble(JsonDocument doc, string prop) =>
        doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static int? GetInt(JsonDocument doc, string prop) =>
        doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static bool? GetBool(JsonDocument doc, string prop) =>
        doc.RootElement.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : null;
}
