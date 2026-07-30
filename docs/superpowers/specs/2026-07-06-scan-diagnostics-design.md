# Scan Diagnostics for AI-Assisted Match Analysis — Design Spec

**Date:** 2026-07-06
**Status:** Draft
**Goal:** Capture complete diagnostic data during card scanning — algorithm decisions, user actions, and corrections — into a single event log that can be exported in an AI-readable format for analyzing and improving match confidence and accuracy.

---

## 1. Problem Statement

When the card matching algorithm makes mistakes (wrong card, wrong printing, low confidence on easy cards), there's no way to send complete diagnostic data to an AI for analysis. The existing `FlagResolution` and `MismatchLog` tables capture some correction data, but miss the algorithm's internal decision (which candidates were considered, why one was picked, what OCR saw) and the full user review journey. Without this, diagnosing matching issues requires reproducing them manually.

## 2. Approach: Single Event Log

One new `ScanDiagnosticEvent` table captures every significant event as a timestamped, typed record. Events form a chronological story per scanned card: what the algorithm decided, then what the user did about it. A purpose-built text export renders these events into a format optimized for AI consumption — not JSON or CSV, but structured plaintext with clear sections, markers, and summary statistics.

The existing `FlagResolution` and `MismatchLog` tables remain untouched. This is an additive parallel system.

## 3. Data Model

### ScanDiagnosticEvent Table

Stored in the collection database (`CollectionDbContext`).

| Column | Type | Purpose |
|--------|------|---------|
| `Id` | int PK | Auto-increment |
| `SessionId` | string | Groups events by scan session (GUID generated when scanning starts) |
| `ScanHash` | ulong | Links events for the same scanned card |
| `EventType` | string | Discriminator: `ScanCompleted`, `UserFlagged`, `UserConfirmed`, `UserCorrected`, `UserUnflagged` |
| `Timestamp` | DateTime | When the event occurred (UTC) |
| `Payload` | string | JSON blob with event-type-specific data |

### Payload Schemas

**ScanCompleted:**
```json
{
  "matchedCardId": "guid-string",
  "matchedName": "Lightning Bolt",
  "matchedSet": "m21",
  "matchedNumber": "199",
  "confidence": 87.5,
  "decisionPhase": "PHashConfident",
  "pHashDistance": 3,
  "artHashDistance": 5,
  "ocrRecognizedName": "Lightning Bolt",
  "ocrNameConfidence": 0.8,
  "ocrDetectedSets": [{"set": "m21", "confidence": 0.92}, {"set": "2xm", "confidence": 0.71}],
  "ocrSymbolConfidence": 0.92,
  "setFilterActive": true,
  "activeSets": ["m21", "2xm"],
  "preferredSets": ["m21"],
  "tieZoneCandidates": [
    {"cardId": "guid", "name": "Lightning Bolt", "set": "m21", "number": "199", "pHashDist": 3, "artHashDist": 5, "setBonus": -5, "finalScore": -2, "selected": true},
    {"cardId": "guid", "name": "Lightning Bolt", "set": "2xm", "number": "141", "pHashDist": 4, "artHashDist": 6, "setBonus": 0, "finalScore": 4, "selected": false}
  ],
  "artHashes": [12345, 67890, 11111],
  "autoFlagReason": "None"
}
```

**UserFlagged:**
```json
{
  "currentCardId": "guid",
  "currentName": "Lightning Bolt",
  "currentSet": "m21",
  "currentConfidence": 87.5,
  "flagReason": "Manual"
}
```

**UserConfirmed:**
```json
{
  "confirmedCardId": "guid",
  "confirmedName": "Lightning Bolt",
  "confirmedSet": "m21",
  "originalConfidence": 87.5
}
```

**UserCorrected:**
```json
{
  "originalCardId": "guid",
  "originalName": "Lightning Bolt",
  "originalSet": "m21",
  "originalConfidence": 87.5,
  "correctedCardId": "guid",
  "correctedName": "Chain Lightning",
  "correctedSet": "leg",
  "correctedNumber": "137",
  "wasInTieZone": false
}
```

**UserUnflagged:**
```json
{
  "cardId": "guid",
  "cardName": "Lightning Bolt",
  "previousFlagReason": "Manual"
}
```

## 4. MatchDiagnostics — Algorithm Decision Capture

### New Types (code-only, not stored in DB)

**MatchDiagnostics** — populated inside `FindClosestMatch()`, exposed on the service:

- `DecisionPhase`: string — `ExactCorrection`, `PHashConfident`, `OcrAssisted`, `ArtHashFallback`
- `PHashDistance`: int — winning candidate's pHash Hamming distance
- `ArtHashDistance`: int? — winning candidate's art hash distance (null if not used)
- `TieZoneCandidates`: List\<TieZoneCandidate\> — all candidates within the competitive range
- `OcrRecognizedName`: string? — OCR text result
- `OcrNameConfidence`: double? — OCR name confidence (0.0-1.0)
- `OcrDetectedSets`: List\<(string SetCode, double Confidence)\>? — set symbol detection results
- `SetFilterActive`: bool — whether a set filter was active during matching
- `ActiveSets`: List\<string\>? — the active set filter codes
- `PreferredSets`: List\<string\>? — sets detected via symbol OCR that got preference bonuses

**TieZoneCandidate** — one per candidate in the competitive zone:

- `CardId`: string
- `Name`: string
- `SetCode`: string
- `CollectorNumber`: string
- `PHashDistance`: int
- `ArtHashDistance`: int? (null if art hash not computed for this candidate)
- `SetBonus`: int (negative = bonus, positive = penalty)
- `FinalScore`: int (pHashDistance + setBonus, used for ranking)
- `Selected`: bool (true for the winner)

### Integration with FindClosestMatch

`ScryfallService` gets a `LastMatchDiagnostics` property (also on `ICardGameService`). `FindClosestMatch()` populates it as a side effect during matching. This avoids changing the method signature which is called from many places. The property is overwritten on each call.

The tie zone candidates are collected from the existing tie-zone logic in `FindClosestMatch()` (the candidates within `TieZone = 4` of the best distance). The decision phase is tagged at each return point in the method.

## 5. Event Capture Points

| Event | Logged In | When |
|-------|-----------|------|
| `ScanCompleted` | `CardService.AddFromStream()` | After matching pipeline completes (after pHash + OCR re-match). Captures `LastMatchDiagnostics` from the game service, the `CardMatch` result, the scan hash, art hashes, and OCR result. |
| `UserFlagged` | `RootViewModel.ToggleFlag()` | When user manually flags a card (flag toggle from unflagged to flagged). |
| `UserConfirmed` | `RootViewModel.ConfirmMatch()` | When user confirms a match is correct. |
| `UserCorrected` | `RootViewModel.AssignMatch()` | When user reassigns to a different card. `wasInTieZone` is determined by querying the `ScanCompleted` event's candidates for this scan hash. |
| `UserUnflagged` | `RootViewModel.ToggleFlag()` | When user unflags a previously flagged card. |

### Session Tracking

A `SessionId` (GUID string) is generated when a scan batch starts. `CardService` holds the current session ID and passes it to `LogScanCompleted()`. A new session ID is generated each time scanning is initiated (e.g., when the user starts a new scan batch). User action events (`UserFlagged`, etc.) look up the `SessionId` from the most recent `ScanCompleted` event for that scan hash. If no `ScanCompleted` event exists (e.g., diagnostics were cleared between scanning and reviewing), the user action event uses a fallback session ID of `"orphaned"` — these still export correctly, just grouped under an "orphaned" session.

## 6. IScanDiagnosticService

New service registered as a singleton in DI.

**Methods:**

- `LogScanCompleted(string sessionId, ulong scanHash, CardMatch? match, MatchDiagnostics? diagnostics, ulong[]? artHashes, OcrMatchResult? ocrResult, FlagReason autoFlagReason)` — creates a `ScanCompleted` event with all algorithm data serialized to JSON
- `LogUserFlagged(ulong scanHash, ScannedCard card)` — creates a `UserFlagged` event
- `LogUserConfirmed(ulong scanHash, ScannedCard card)` — creates a `UserConfirmed` event
- `LogUserCorrected(ulong scanHash, ScannedCard card, CardMatch newMatch)` — creates a `UserCorrected` event, looks up tie zone from `ScanCompleted` to populate `wasInTieZone`
- `LogUserUnflagged(ulong scanHash, ScannedCard card, FlagReason previousReason)` — creates a `UserUnflagged` event
- `ExportDiagnostics(string filePath)` — reads all events, groups by session then by scan hash, renders the AI-readable text format, writes to file
- `ClearDiagnostics()` — deletes all rows from the `ScanDiagnosticEvent` table
- `GetEventCount()` — returns total row count for UI display

The service handles all JSON serialization internally. Callers pass typed parameters.

## 7. Export Format

The export produces a `.txt` file structured for AI analysis. The format is:

```
=== SCAN DIAGNOSTIC EXPORT ===
App Version: {version}
Exported: {utc timestamp}
Total Sessions: {count}
Total Events: {count}

================================================================
SESSION: {sessionId}
Started: {first event timestamp}
Cards Scanned: {count}
Flags Raised: {count}
Corrections Made: {count}
================================================================

--- CARD: scan_hash=0x{hash hex} ---
SCAN RESULT [{timestamp}]
  Decision: {phase} (Phase {N})
  Match: {name} | {set} #{number} | Confidence: {pct}%
  pHash Distance: {N} | Art Hash Distance: {N}
  OCR Name: "{text}" (confidence: {0.00})
  OCR Sets Detected: {set} ({confidence}), ...
  Set Filter: {ON|OFF} | Active Sets: {csv} | Preferred: {csv}
  Auto-Flag: {reason}
  Tie Zone ({N} candidates):
    > [SELECTED] {name} | {set} #{number} | pHash: {N}, artHash: {N}, setBonus: {N}, finalScore: {N}
      {name} | {set} #{number} | pHash: {N}, artHash: {N}, setBonus: {N}, finalScore: {N}
  {USER ACTION [{timestamp}]}
    ...
  OUTCOME: {No user action | Confirmed | Corrected | Flagged then unflagged}

=== SUMMARY STATISTICS ===
Total Scans: {N}
Auto-Accepted (no user action): {N}
User Confirmed: {N}
User Corrected: {N}
  Correct card was in tie zone: {N} ({pct}%)
  Correct card was NOT in tie zone: {N} ({pct}%)
User Flagged then Unflagged: {N}
Average Confidence (all scans): {pct}%
Average Confidence (corrected scans): {pct}%
Decision Phase Breakdown:
  ExactCorrection: {N}
  PHashConfident: {N}
  OcrAssisted: {N}
  ArtHashFallback: {N}
High-Confidence Mismatches (>=80% confidence, user corrected): {N}
  {name} | {set} #{number} -> {name} | {set} #{number} | Was {pct}% confident | {IN|NOT in} tie zone
```

Key format decisions:
- **`> [SELECTED]` marker** in tie zone for the winning candidate
- **OUTCOME line** per card summarizes what happened
- **Summary statistics** enable quick pattern recognition (e.g., "75% of corrections were in tie zone" = scoring/tiebreaking issue; "correct card NOT in tie zone" = hash distance threshold too tight)
- **High-confidence mismatches listed individually** — these are the most actionable items

## 8. UI Changes

Minimal additions to the existing diagnostic/settings area:

- **Update existing "Clear Diagnostic Logs" button** to also clear the `ScanDiagnosticEvent` table
- **Add "Export Diagnostics..." button** that opens a save file dialog, calls `ExportDiagnostics(filePath)`. Default filename: `omnicard-diagnostics-{yyyy-MM-dd}.txt`
- **Event count label**: "{N} diagnostic events" displayed near the buttons so the user knows there's data before clearing

No changes to the scan UI. Diagnostic logging is silent background activity.

## 9. Testing

### New Tests
- `ScanDiagnosticServiceTests`: Event logging (all 5 types), JSON payload correctness, `wasInTieZone` lookup, clear, event count
- `MatchDiagnosticsTests`: Verify `FindClosestMatch()` populates `LastMatchDiagnostics` with correct decision phase, tie zone candidates, and distances for various match scenarios (exact correction, pHash confident, OCR-assisted, art hash fallback)
- `DiagnosticExportTests`: Verify export format — correct sections, candidate formatting, summary statistics calculations, empty export handling

### Updated Tests
- Existing `FindClosestMatch` tests may need updating if the method signature or internal structure changes to accommodate diagnostics population

## 10. Out of Scope

- Replacing existing `FlagResolution` or `MismatchLog` tables — these remain as-is
- Real-time diagnostic dashboard or visualization — this is export-only
- Automatic upload/sharing of diagnostic files — user manually sends the exported file
- Import of diagnostic files for in-app analysis — analysis happens externally via AI
