# ManaBox Scan Queue Export Design

**Date:** 2026-07-10
**Branch:** feat/manabox-scan-export
**Goal:** After scanning, allow exporting the scan queue directly to ManaBox-native formats (CSV and text) as an alternative to committing to the local collection. On export, clear the queue and delete temp images.

## Context

The app currently supports four CSV export formats for the committed collection (`ExportAppNative`, `ExportTcgPlayer`, `ExportMoxfield`, `ExportManabox`). However:
- There is no way to export the **scan queue** directly — users must commit to collection first.
- The existing `ExportManabox` format uses incorrect column names (`Card Name`, `Finish`, etc.) that don't match the actual ManaBox native format (`Name`, `Foil`, etc.).

Users who scan cards for inventory tracking in ManaBox need a fast path: scan → export → import into ManaBox, without persisting to the local collection or keeping scan images.

## Changes

### 1. Fix Existing ExportManabox Format

Replace the column layout in `CsvExportImportService.ExportManabox` to match the real ManaBox native format. The method signature stays the same: `ExportManabox(string filePath, IEnumerable<CollectionCard> cards)`.

**New columns** (15 columns, no `ManaBox ID`):
```
Name,Set code,Set name,Collector number,Foil,Rarity,Quantity,Scryfall ID,Purchase price,Misprint,Altered,Condition,Language,Purchase price currency,Added
```

**Field mappings (CollectionCard → CSV):**
| Column | Value |
|--------|-------|
| Name | `card.Name` |
| Set code | `card.SetCode` |
| Set name | `card.SetName` |
| Collector number | `card.Number` |
| Foil | `card.IsFoil ? "foil" : "normal"` |
| Rarity | `card.Rarity` |
| Quantity | `1` |
| Scryfall ID | `card.GameCardId` |
| Purchase price | `card.PurchasePrice?.ToString(InvariantCulture) ?? ""` |
| Misprint | `false` |
| Altered | `false` |
| Condition | mapped via `ConditionToManabox` dict (see below) |
| Language | `"en"` |
| Purchase price currency | `"USD"` |
| Added | `card.DateAdded.ToString("o")` |

**Condition mapping** (internal short code → ManaBox snake_case):
| Internal | ManaBox |
|----------|---------|
| `NM` | `near_mint` |
| `LP` | `lightly_played` |
| `MP` | `moderately_played` |
| `HP` | `heavily_played` |
| `D` | `damaged` |

**Update format detection** in `DetectFormat`: change from detecting `"Finish" + "Card Name"` to detecting `"Foil" + "Scryfall ID" + "Purchase price currency"` (unique combination that distinguishes ManaBox from other formats).

**Update `ParseManaboxRow`**: read `"Foil"` column (`"foil"` → `IsFoil=true`) instead of `"Finish"`, read `"Condition"` as snake_case and reverse-map to internal codes.

### 2. New Scan Export Methods

Add three new methods to `ICsvExportImportService`:

```csharp
void ExportManaboxScans(string filePath, IEnumerable<ScannedCard> scans);
void ExportManaboxScansCollection(string filePath, IEnumerable<ScannedCard> scans);
void ExportManaboxScansText(string filePath, IEnumerable<ScannedCard> scans);
```

All three methods skip cards where `scan.Match == null`.

#### ExportManaboxScans — Single-Location CSV

Same 15 columns as the corrected `ExportManabox`, with field values sourced from `ScannedCard`:

| Column | Value |
|--------|-------|
| Name | `scan.Match.Name` |
| Set code | `scan.Match.SetCode` |
| Set name | `scan.Match.SetName` |
| Collector number | `scan.Match.CollectorNumber` |
| Foil | `scan.IsFoil ? "foil" : "normal"` |
| Rarity | `scan.Match.Rarity` |
| Quantity | `1` |
| Scryfall ID | `scan.Match.GameSpecificId` |
| Purchase price | `scan.PurchasePrice?.ToString(InvariantCulture) ?? ""` |
| Misprint | `false` |
| Altered | `false` |
| Condition | mapped via `ConditionToManabox` dict |
| Language | `"en"` |
| Purchase price currency | `"USD"` |
| Added | `DateTime.UtcNow.ToString("o")` |

#### ExportManaboxScansCollection — Full Collection CSV

Same as above with two columns prepended:

```
Binder Name,Binder Type,Name,Set code,...
```

| Column | Value |
|--------|-------|
| Binder Name | `scan.OverrideContainer?.Name ?? "Scans"` |
| Binder Type | `scan.OverrideContainer != null ? scan.OverrideContainer.ContainerType.ToString().ToLowerInvariant() : "list"` |

#### ExportManaboxScansText — Plain Text

One line per card, format:
```
{quantity} {name} ({set code}) {collector number} *F*
```

- `*F*` suffix only for foil cards (`scan.IsFoil == true`)
- Non-foil cards have no suffix (line ends after collector number)
- Quantity is always `1`

Example output:
```
1 Super Villain Lockup (MSH) 37 *F*
1 Iron Man, Armored Avenger (MSC) 33
```

### 3. ViewModel Integration

Three new `[RelayCommand]` methods on `RootViewModel`:

- `ExportScansManaboxCsv()` — Save File dialog → `ExportManaboxScans` → clear queue
- `ExportScansManaboxCollectionCsv()` — Save File dialog → `ExportManaboxScansCollection` → clear queue
- `ExportScansManaboxText()` — Save File dialog → `ExportManaboxScansText` → clear queue

**Post-export cleanup** (all three commands, on successful export):
1. `CardService.ClearTempFiles()` — deletes all temp PNG files
2. `CardService.ScannedCards.Clear()` — clears the observable collection
3. Reset selection state (`SelectedScannedCards = []`, `SelectedScannedCard = null`)
4. `NotifySelectionChanged()`
5. Message: `"Exported {count} cards to {filename}. Scan queue cleared."`

**Guard conditions:**
- Disabled when `CardService.ScannedCards.Count == 0`
- Disabled during audit mode (`IsAuditMode`)

### 4. Testing

**Update existing tests:**
- `ExportManabox_WritesAllManaboxColumns` — verify new column names, condition format, no `ManaBox ID`
- `PreviewImport_DetectsManaboxFormat` — update format detection for new columns
- `PreviewImport_ManaboxFinish_MapsFoilCorrectly` — use `Foil` column with `"foil"`/`"normal"`

**New tests (11):**

| Test | Description |
|------|-------------|
| `ExportManaboxScans_WritesCorrectColumns` | Header matches expected 15-column order, data populated from ScannedCard.Match |
| `ExportManaboxScans_SkipsUnmatchedCards` | Cards with `Match == null` excluded from output |
| `ExportManaboxScans_MapsFoilCorrectly` | `IsFoil=true` → `"foil"`, `false` → `"normal"` |
| `ExportManaboxScans_MapsConditionCorrectly` | `"NM"` → `"near_mint"`, `"LP"` → `"lightly_played"` |
| `ExportManaboxScans_EmptyQueue_WritesHeaderOnly` | Empty input produces header-only file |
| `ExportManaboxScansCollection_IncludesBinderColumns` | First two columns are `Binder Name,Binder Type` |
| `ExportManaboxScansCollection_DefaultsToScansForUnassigned` | No OverrideContainer → `"Scans"/"list"` |
| `ExportManaboxScansCollection_UsesOverrideContainer` | OverrideContainer set → uses container name and type |
| `ExportManaboxScansText_WritesCorrectFormat` | Output matches `1 Card Name (SET) 42` pattern |
| `ExportManaboxScansText_AppendsFoilMarker` | Foil cards get ` *F*` suffix, non-foil don't |
| `ExportManaboxScansText_SkipsUnmatchedCards` | Null match cards excluded |

## Out of Scope

- No UI changes beyond wiring the commands (button placement is existing infrastructure)
- No changes to import logic beyond updating column detection for the corrected format
- No changes to CommitScans flow
- No ManaBox API integration — this is file-based export only
