# Manual Card Entry — Design Spec

## Context

Foil cards are difficult to scan reliably. Users need a way to add cards to their collection by searching the game database by name and adding them directly, without scanning. This bypasses the scan pipeline entirely.

## Feature Overview

An "Add Cards" dialog accessible from the collection tab toolbar and from location context menus. The dialog uses the existing `CardSearchControl` to search the game database, lets the user set card properties (condition, foil, price, quantity), pick a storage location, and add directly to the collection. The dialog stays open for batch entry — add multiple cards in one session.

## Dialog Layout

```
┌─ Add Cards ──────────────────────────────────────────┐
│                                                      │
│  [CardSearchControl — search box + results list]     │
│                                                      │
│  ─── Card Properties ───                             │
│  Condition: [NM ▼]  Foil: [ ]  Price: [____]        │
│  Quantity: [1 ▲▼]                                    │
│                                                      │
│  ─── Location ───                                    │
│  Container: [selected location ▼]                    │
│  Page: [__]  Slot: [__]  Section: [________]         │
│                                                      │
│  [Add to Collection]              3 cards added      │
└──────────────────────────────────────────────────────┘
```

## Entry Points

1. **Collection tab toolbar** — "Add Card" button. Uses the currently active container filter (if any) as the default location.
2. **Location tile context menu** — "Add Card" option on right-click. Pre-selects the right-clicked location as the container.

Both open the same dialog with different default container values.

## Data Flow

1. User types a card name → `ICardGameService.SearchCards(query)` returns `List<CardMatch>`
2. User selects a `CardMatch` from the results
3. User sets condition, foil, price, quantity, location
4. "Add to Collection" → calls new `ICardService.AddCardToCollection()` method
5. Method creates `CollectionCard` entity from `CardMatch` data + user-specified properties
6. Saves to SQLite via `CollectionDbContext`
7. Dialog clears search, increments count, ready for next card
8. On close → collection view refreshes

## Backend: ICardService.AddCardToCollection

New method on `ICardService` (interface in `OmniCard.Shared/Interfaces/`):

```csharp
void AddCardToCollection(
    CardMatch match,
    CardGame game,
    string condition,
    bool isFoil,
    decimal? purchasePrice,
    int quantity,
    StorageContainer? container,
    int? page,
    int? slot,
    string? section);
```

Implementation in `OmniCard.Collection/CardService.cs`:
- Creates `quantity` number of `CollectionCard` entries
- Populates from `CardMatch`: Name, SetCode, SetName, Number, Rarity, ImageUri, GameCardId
- Sets Color and CardType via `CardAttributeExtractor` (same as CommitScans)
- Sets user properties: Condition, IsFoil, PurchasePrice
- Sets location: ContainerId, Page, Slot, Section
- No scan image (ScanImagePath = null) — this is expected and the UI already handles cards without scan images

## Reused Components

- `CardSearchControl` (OmniCard.Controls) — search box + results list
- `ICardGameService.SearchCards()` — game database search
- `CardAttributeExtractor.ExtractColor/ExtractCardType()` — card attribute population
- Same location picker pattern (container dropdown, page/slot/section fields) as scanner tab

## Files to Create/Modify

| File | Action |
|------|--------|
| `OmniCard/Views/ManualAdd/ManualAddView.xaml` | Create — dialog XAML |
| `OmniCard/Views/ManualAdd/ManualAddView.xaml.cs` | Create — code-behind |
| `OmniCard/Views/ManualAdd/ManualAddViewModel.cs` | Create — ViewModel |
| `OmniCard.Shared/Interfaces/ICardService.cs` | Modify — add `AddCardToCollection` method |
| `OmniCard.Collection/CardService.cs` | Modify — implement `AddCardToCollection` |
| `OmniCard/Views/Root/CollectionTabView.xaml` | Modify — add toolbar button |
| `OmniCard/Views/Root/CollectionViewModel.cs` | Modify — add command to open dialog |
| `OmniCard/Views/Root/LocationOverviewView.xaml` | Modify — add context menu item |
| `OmniCard/App.xaml.cs` | Modify — register ManualAddView/ViewModel as transients |

## Verification

1. Click "Add Card" in collection toolbar → dialog opens
2. Search for "Lightning Bolt" → results appear with card images
3. Select a result, set foil, click "Add to Collection" → count shows "1 card added"
4. Search for another card, add it → count shows "2 cards added"
5. Close dialog → collection refreshes, both cards visible in the selected location
6. Right-click a location tile → "Add Card" → dialog opens with that location pre-selected
7. Cards added without scan images display correctly (API image used instead)
