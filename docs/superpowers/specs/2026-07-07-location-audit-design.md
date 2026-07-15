# Location Audit Tool

**Date:** 2026-07-07
**Status:** Approved

## Goal

Enable users to audit a storage location by scanning its cards and comparing the scan results to what the database says should be there. Produces a report showing expected vs actual counts, missing cards, and extra cards, with PDF export.

## Requirements

1. Audit a location by scanning its cards and comparing to DB inventory
2. Entry points: right-click location tile context menu + button in card list toolbar
3. Matching scoped to only the cards in the audited location (temporary hash index from those cards' Scryfall entries)
4. Scanner UI looks the same but scans are always temporary (never committable)
5. One-to-one matching: each scanned card consumes one expected card by `GameCardId`
6. Match by card identity only (`GameCardId`) — ignore condition and foil status
7. Report dialog shows: expected count, actual count, matched cards, missing cards, extra cards
8. User can manually assign card identity to unmatched items via Scryfall search (display-only, does not modify collection)
9. Export report to PDF
10. Report dialog displays results on screen

## Design

### 1. Audit Service

**New file: `OmniCard/Services/AuditService.cs`**

Encapsulates audit lifecycle: start, scoped matching, report generation.

```csharp
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
```

**Scoped matching:**

When `StartAudit` is called:
1. Read the location's `CollectionCard` entries to get their `GameCardId` values
2. Query the Scryfall DB for the perceptual hashes of just those cards
3. Build a temporary in-memory hash index: `List<(Guid Id, ulong Hash)>` and art hash equivalent
4. Store the location's expected card list (with duplicates) for report generation

`FindScopedMatch` uses the same Hamming distance / tie-zone logic as `ScryfallService.FindClosestMatch` but searches only the scoped index. This avoids modifying the singleton `ScryfallService` caches. The audit service owns its scoped cache and discards it on `EndAudit`.

**Report generation (`GenerateReport`):**

One-to-one matching by `GameCardId`:
1. Build a bag (list with duplicates) of expected `GameCardId` values from the location
2. For each scanned card with a match, try to consume one instance from the bag
3. Remaining bag entries -> Missing (in location but not scanned)
4. Scanned cards that couldn't consume from the bag -> Extra (scanned but not in location)
5. Successfully consumed pairs -> Matched

**Report model:**

**New file: `OmniCard/Models/AuditReport.cs`**

```csharp
public class AuditReport
{
    public string LocationName { get; init; }
    public DateTime GeneratedAt { get; init; }
    public int ExpectedCount { get; init; }
    public int ActualCount { get; init; }
    public List<AuditReportItem> Matched { get; init; }
    public List<AuditReportItem> Missing { get; init; }
    public List<AuditReportItem> Extra { get; init; }
}

public class AuditReportItem
{
    public string? Name { get; set; }
    public string? SetCode { get; set; }
    public string? SetName { get; set; }
    public string? CollectorNumber { get; set; }
    public string? ImageUri { get; set; }
    public string? GameCardId { get; set; }
    public double? Confidence { get; set; }
    public bool IsManuallyAssigned { get; set; }
}
```

`AuditReportItem` is mutable because users can manually assign card identity to Missing and Extra items in the report dialog.

### 2. Scanner Integration — Audit Mode

**Modifications to `RootViewModel`:**

New properties:
- Delegates to `IAuditService.IsAuditActive`, `AuditLocationName` for UI binding

New commands:
- `StartAudit(int containerId)`: activates audit mode, starts the audit service, switches to scanner tab
- `EndAudit()`: clears scan queue, ends audit service, returns to previous view
- `GenerateAuditReport()`: calls `AuditService.GenerateReport` with current scan queue, opens report dialog

**Scanning pipeline changes:**

The branching happens in `CardSevice.AddFromStream`. The `AuditService` is injected into `CardSevice`. When `AuditService.IsAuditActive`:
- Call `AuditService.FindScopedMatch(hash, artHashes)` instead of `FindBestMatch`
- Skip async OCR re-matching (scoped index makes it unnecessary — the candidate pool is small)
- Auto-flag as `NoMatch` if scoped match returns null (card not expected in location)

**Scanner tab UI changes (audit mode):**

When `IsAuditActive == true`:
- **Banner** at top of scanner tab: "Auditing: [Location Name]" with "Cancel Audit" button
- **Hidden controls**: "Commit Scans to Collection" button, "Scans Verified" toggle, location dropdown, page/slot/section fields, purchase price, foil checkbox
- **New button**: "Generate Audit Report" replaces the commit button area
- **Remaining visible**: Scan quality dropdown, "Scan Cards..." button, "Reprocess Unmatched", "Clear Queue", set filter controls, scan queue list with detail panel

All existing scan queue features (manual search, flagging, condition, sort/filter) remain functional.

**Entry points:**

1. **Location tile context menu** (`LocationOverviewView.xaml`): Add "Audit Location..." menu item. Click handler calls `RootViewModel.StartAudit(containerId)`.

2. **Card list toolbar** (`CollectionTabView.xaml`): Add "Audit" button visible when viewing a single location (`ShowCardList == true && !ShowAllCards`). Calls `RootViewModel.StartAudit(CurrentLocationId)`.

**Ending audit:**
- "Cancel Audit" button clears scan queue and exits audit mode
- After generating a report, the user can continue scanning or cancel
- Navigating away from the scanner tab does NOT end the audit

### 3. Audit Report Dialog

**New files:**
- `OmniCard/Views/AuditReport/AuditReportView.xaml` + `.xaml.cs`
- `OmniCard/Views/AuditReport/AuditReportViewModel.cs`

**Dialog layout:**

1. **Summary header**: Location name, date/time, expected count, actual count, match rate percentage

2. **Three expandable sections (or tabs)**:
   - **Matched** (count badge) — cards successfully paired. Each row: card name, set, collector number.
   - **Missing from scan** (count badge) — in location DB but not scanned. Each row: card name, set, collector number. "Assign..." button per row opens Scryfall search to label the item.
   - **Not in location** (count badge) — scanned but not matched to location. Each row: scan thumbnail (if available), card name (if matched or assigned), set, collector number. "Assign..." button per row opens Scryfall search.

3. **Footer**: "Export to PDF" button + "Close" button

**Manual assignment:**

When user clicks "Assign..." on a Missing or Extra item, open the existing manual search pattern (search Scryfall DB, pick a card). The selected `CardMatch` populates the `AuditReportItem` fields and sets `IsManuallyAssigned = true`. This is display-only — no collection or scan queue modification.

**Dialog integration:**

Add to `IDialogService`:
```csharp
void ShowAuditReport(AuditReport report);
```

Implementation creates `AuditReportView`, sets `ViewModel.Load(report)`, shows as modal dialog.

### 4. PDF Export

**NuGet package:** QuestPDF (MIT license, fluent C# API, no external dependencies)

**New file: `OmniCard/Services/AuditPdfExporter.cs`**

Generates a PDF from an `AuditReport` model:
- Header: "Audit Report — [Location Name]" + date/time
- Summary box: Expected / Actual / Matched / Missing / Extra counts
- Table: Missing cards (Name, Set, Collector Number)
- Table: Extra cards (Name or "Unidentified", Set, Collector Number)
- Table: Matched cards (Name, Set, Collector Number)

The PDF reflects any manual assignments made in the dialog.

Triggered by the "Export to PDF" button in the report dialog, which opens a save-file dialog, then calls the exporter.

### 5. What Does Not Change

- Normal scanning workflow (non-audit mode is untouched)
- Collection database (audit is read-only + scan-only, no writes)
- `ScryfallService` singleton caches (audit builds its own scoped cache)
- Existing "Scans Verified" / audit-complete toggle (separate concept — verifying scans before commit)
- Sealed products tab
