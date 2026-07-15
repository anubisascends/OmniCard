# Decklist Check Feature

**Date:** 2026-07-12
**Status:** Approved

## Overview

Import a decklist from Moxfield or Archidekt by pasting a URL, cross-reference it against the user's collection, and generate a printable PDF report showing which cards are owned (with physical locations) and which need to be purchased (with market prices).

## User Flow

1. User clicks **"Check Decklist..."** in the Tools menu.
2. A dialog appears with a text box for the URL and a "Fetch" button.
3. The app parses the URL, detects the source (Moxfield or Archidekt), and calls the API.
4. If the API call fails, the dialog shows a text area with a message: "Couldn't reach the site. Paste your decklist here instead:" and a "Parse" button.
5. Once the card list is obtained, the app cross-references against the collection and shows a preview: summary counts (owned/missing), the deck name, and total cost to complete.
6. User clicks "Generate Report" and gets a Save File dialog for the PDF.
7. QuestPDF generates the report and saves it.

## Decklist Fetching

### URL Parsing

- **Moxfield:** Extract deck ID from `moxfield.com/decks/{publicId}` -- call `GET https://api2.moxfield.com/v2/decks/all/{publicId}`
- **Archidekt:** Extract numeric ID from `archidekt.com/decks/{id}/...` -- call `GET https://archidekt.com/api/decks/{id}/`
- Invalid URLs show a validation message in the dialog.

### API Response Mapping

Both APIs return different JSON shapes. Responses are normalized to a common model:

```csharp
DecklistEntry { int Quantity, string CardName, string? SetCode, string? CollectorNumber }
```

The deck name is also extracted from the API response.

### Text Fallback Parser

Standard MTG text format, one card per line:

- `1 Lightning Bolt` (name only)
- `1 Lightning Bolt (M11) 149` (with set and collector number)
- Lines starting with `//` or empty lines are ignored.
- Sideboard and mainboard sections are treated as one flat list.

### HTTP Details

- Uses existing `IHttpClientFactory`.
- User-Agent: `OmniCard/1.0`.
- 10-second timeout.
- No auth required (public decks only).

## Collection Matching

### Match Process

For each `DecklistEntry`, search the collection database:

1. **Exact set match first:** Query `CollectionCard` where `Name` matches AND `SetCode` matches the decklist's set code. These get flagged as "exact set match" in the report.
2. **Any printing fallback:** If not enough copies found from the exact set, query by `Name` only across all sets to find remaining copies.
3. **Quantity tracking:** If the decklist needs 4x Lightning Bolt and the user owns 3 total (1 exact set, 2 other sets), the report shows all 3 locations and lists 1 as "need to buy."

### Output Model

```
DecklistCheckResult
  DeckName: string
  DeckSource: string (Moxfield / Archidekt / Text)
  OwnedEntries: list of
    CardName, SetCode, CollectorNumber, QuantityNeeded,
    Locations: list of
      ContainerName, Page, Slot, Section, SetCode, IsFoil, IsExactSetMatch
  MissingEntries: list of
    CardName, SetCode, CollectorNumber, QuantityNeeded, MarketPrice (per copy)
  TotalOwned: int
  TotalMissing: int
  EstimatedCost: decimal (sum of missing cards' market prices)
```

### Price Lookup

For missing cards, resolve the card name to a `gameCardId` via `ICardGameService.SearchCards(cardName)` (preferring the exact set match if the decklist specifies one), then call `GetCurrentPrice(gameCardId, isFoil: false)` for the market price. Cards not found in the local Scryfall database show price as "N/A."

## PDF Report Layout

**Page setup:** Letter size (8.5" x 11"), 40pt margins, 10pt base font. Same style as the existing audit report (QuestPDF, Community license).

### Header

- Title: "Decklist Report"
- Deck name and source (e.g., "Imported from Moxfield")
- Date generated
- Summary line: "Owned: 87/100 | Missing: 13 | Estimated cost: $42.50"

### Section 1 -- "Cards You Own"

| Card Name | Set | Qty Needed | Location(s) |
|---|---|---|---|
| Lightning Bolt | M11 | 1 | **Binder A** - Page 3, Slot 2 (M11, exact match); Bulk (2ED) |

- Location entries that are an exact set match are bolded or marked with a star.
- Multiple locations listed inline, separated by semicolons.
- Sorted alphabetically by card name.

### Section 2 -- "Cards to Buy"

| Card Name | Set | Qty | Market Price | Subtotal |
|---|---|---|---|---|
| Ragavan, Nimble Pilferer | MH2 | 1 | $55.00 | $55.00 |

- Sorted by subtotal descending (most expensive first).
- Footer row with total estimated cost.

### Footer

Page numbers.

## Architecture & File Organization

### New Files

| File | Purpose |
|---|---|
| `OmniCard.Shared/Models/DecklistEntry.cs` | Normalized card entry (Quantity, CardName, SetCode, CollectorNumber) |
| `OmniCard.Shared/Models/DecklistCheckResult.cs` | Full result model (owned/missing entries, totals, prices) |
| `OmniCard.Shared/Interfaces/IDecklistService.cs` | Interface: FetchDecklist, ParseDecklistText, CheckAgainstCollection |
| `OmniCard.Collection/DecklistService.cs` | Implementation: API fetching, text parsing, collection matching |
| `OmniCard.Audit/DecklistPdfExporter.cs` | QuestPDF report generation (alongside existing AuditPdfExporter) |
| `OmniCard.Shared/Interfaces/IDecklistPdfExporter.cs` | Interface for the exporter |
| `OmniCard/Views/DecklistCheck/DecklistCheckView.xaml` | Dialog UI |
| `OmniCard/Views/DecklistCheck/DecklistCheckViewModel.cs` | Dialog logic |

### Modifications to Existing Files

| File | Change |
|---|---|
| `App.xaml.cs` | Register DecklistService, DecklistPdfExporter, View + ViewModel in DI |
| `DialogService.cs` / `IDialogService.cs` | Add `ShowDecklistCheck()` method |
| `RootViewModel.cs` | Add `CheckDecklistCommand` to wire up the menu item |
| `RootView.xaml` | Add "Check Decklist..." menu item under Tools |

### Dependencies

No new NuGet packages. Reuses existing `IHttpClientFactory`, `System.Text.Json`, QuestPDF, and EF Core.
