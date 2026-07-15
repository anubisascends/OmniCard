# Decklist Report Enhancements

**Date:** 2026-07-12
**Status:** Approved

## Overview

Enhance the decklist check reports in two ways: (1) group cards by type category (Creature, Instant, etc.) in the existing summary report, and (2) add a new detailed report that includes card images and full card information.

## Type Extraction

Extract a broad type category from the Scryfall `Card.TypeLine` field by checking for keywords in priority order:

1. Planeswalker
2. Creature (includes "Artifact Creature", etc.)
3. Instant
4. Sorcery
5. Artifact
6. Enchantment
7. Land
8. Other

This category is stored on both `OwnedDecklistEntry` and `MissingDecklistEntry` as `TypeCategory`.

## Data Model Changes

Add the following fields to both `OwnedDecklistEntry` and `MissingDecklistEntry`:

- `string? TypeCategory` -- broad type group (Creature, Instant, etc.)
- `string? TypeLine` -- full type line from Scryfall (e.g., "Creature — Human Pirate")
- `string? ManaCost` -- mana cost string (e.g., "{2}{R}")
- `string? OracleText` -- rules text
- `string? Power` -- power (creatures only)
- `string? Toughness` -- toughness (creatures only)
- `string? Rarity` -- rarity string
- `string? ImageUri` -- Scryfall image URL (Normal preferred)
- `string? LocalImagePath` -- local cached image path

These fields are populated during `CheckAgainstCollection` by looking up the card via `SearchCards` and extracting from the `Card` object in `CardMatch.Source`.

## Summary Report Changes

The existing summary PDF is updated to group both "Cards You Own" and "Cards to Buy" by type category:

- Each type category gets a subheader with count (e.g., "Creatures (12)")
- Cards within each group are sorted alphabetically
- Type groups are ordered by the priority list above
- Empty groups are omitted
- Table columns remain the same (Name, Set, Qty, Location(s) for owned; Name, Set, Qty, Price, Subtotal for missing)
- The total cost footer on the missing section remains unchanged

## Detailed Report

A new PDF report with one card per row, grouped by type category with the same ordering as the summary.

### Owned Card Row Layout

```
[Card Image]  Card Name                  Mana Cost
  ~1in wide    Full Type Line             P/T (if creature)
              "Oracle text..."
              Set: XXX | Rarity: Rare
              Location: Container, Page X, Slot Y (SET, ★ if exact match)
```

### Missing Card Row Layout

```
[Card Image]  Card Name                  Mana Cost
  ~1in wide    Full Type Line             P/T (if creature)
              "Oracle text..."
              Set: XXX | Rarity: Rare
              Market Price: $XX.XX
```

### Image Loading

1. Try `LocalImagePath` first -- if the file exists on disk, use it
2. If not found, download from `ImageUri` into a temp directory for PDF generation
3. If both fail, render a placeholder box with the card name

Image download uses the existing `IHttpClientFactory`.

### Header and Footer

Same as the summary report: deck name, source, date, owned/missing/cost summary line, page numbers.

## Dialog Changes

Replace the single "Generate Report" button with two buttons:

- **"Summary Report"** -- generates the type-grouped summary PDF
- **"Detailed Report"** -- generates the detailed PDF with images

Both use the standard `SaveFileDialog` and are disabled until results are loaded.

## Interface Changes

`IDecklistPdfExporter` gets a second method:

```
void ExportDetailed(DecklistCheckResult result, string filePath, IHttpClientFactory httpClientFactory)
```

The `IHttpClientFactory` parameter is needed for downloading card images that aren't cached locally.

## Files Modified

| File | Change |
|---|---|
| `OmniCard.Shared/Models/DecklistCheckResult.cs` | Add type/detail fields to entry records |
| `OmniCard.Collection/DecklistService.cs` | Populate type category and card detail fields during matching |
| `OmniCard.Shared/Interfaces/IDecklistPdfExporter.cs` | Add `ExportDetailed` method |
| `OmniCard.Audit/DecklistPdfExporter.cs` | Group summary by type, implement detailed report |
| `OmniCard/Views/DecklistCheck/DecklistCheckViewModel.cs` | Add `GenerateDetailedReportCommand` |
| `OmniCard/Views/DecklistCheck/DecklistCheckView.xaml` | Replace single button with two buttons |
| `OmniCard/Views/DecklistCheck/DecklistCheckView.xaml.cs` | Wire second export callback |

## Dependencies

No new NuGet packages. Uses existing `IHttpClientFactory`, QuestPDF, and Scryfall data.
