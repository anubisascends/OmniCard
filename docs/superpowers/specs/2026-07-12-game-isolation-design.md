# Game Isolation Design — Magic vs One Piece

**Date:** 2026-07-12
**Status:** Approved

## Problem

The OmniCard codebase supports both Magic: The Gathering and One Piece TCG via the `ICardGameService` interface. While the service layer routes operations through the correct game service, the UI/ViewModel layer has gaps where game state bleeds across boundaries:

- Switching games while scans are pending leaves stale cards in the queue
- Set filters persist across game switches (MTG filter codes applied to One Piece)
- No rate limiting on API refresh, risking overload on the optcgapi.com VPS
- Set symbol preloading fires for all games but only MTG has an SVG source

## Scope

Minimal guardrails at the ViewModel/UI layer. The service architecture (`CardService`, `ICardGameService`, per-game `DbContext`) is already correctly isolated — this work enforces that isolation at the interaction boundary.

**In scope:**
- Block game switching when scans are pending
- Clear set filter on game switch
- 24-hour cooldown on card data refresh (per-game)
- Skip set symbol preload for non-MTG games

**Out of scope:**
- Art hash computation for One Piece (separate feature)
- OCR symbol detection for One Piece (separate feature)
- Per-game scan queues (user chose warn-and-block over separate queues)
- Per-game set filter persistence (user chose clear-on-switch)

## Design

### 1. Game Switch Guard

**File:** `OmniCard/Views/Root/RootViewModel.cs` — `OnSelectedGameChanged`

When the user selects a different game and `CardService.ScannedCards.Count > 0`:

1. Show a `MessageBox`:
   > "You have {n} unconfirmed scan(s). Please commit or discard them before switching games."
2. Revert `SelectedGame` to its previous value
3. Use a `_suppressGameChangeHandler` bool flag to prevent the revert from re-triggering the handler

When the scan queue is empty, the switch proceeds normally.

### 2. Set Filter Reset on Game Switch

**File:** `OmniCard/Views/Root/RootViewModel.cs` — `OnSelectedGameChanged`

After a successful game switch (no pending scans), reset the set filter:

- Set `SetFilterText = ""`, which triggers the existing `OnSetFilterTextChanged` → `UpdateSetFilter()` cascade
- This clears `CardService.SelectedSetFilter`

User starts fresh with "all sets" for the new game.

### 3. 24-Hour Refresh Cooldown

**File:** `OmniCard/Views/Root/RootViewModel.cs` — `RefreshCardData` command

**Storage:** A JSON file at the data path: `refresh-timestamps.json`
```json
{
  "Mtg": "2026-07-12T10:30:00Z",
  "OnePiece": "2026-07-11T08:00:00Z"
}
```

**Behavior:**
1. Before calling `DownloadBulkDataAsync`, read the timestamp for the current `SelectedGame`
2. If less than 24 hours ago, show a `MessageBox`:
   > "Card data for {game} was last refreshed {timeAgo}. Refresh is available once every 24 hours to minimize API load. Next refresh available at {timestamp}."
3. Cancel the refresh
4. If 24+ hours or no previous timestamp, proceed normally
5. Write the new timestamp after successful refresh

**Force Refresh:** The cooldown dialog uses Yes/No buttons — "Click Yes to refresh anyway, or No to cancel." A single dialog is cleaner UX than a two-step confirmation flow.

### 4. Skip Set Symbol Preload for Non-MTG

**File:** `OmniCard/Views/Root/RootViewModel.cs` — `RefreshCardData` command

Guard the `setSymbolCache.PreloadSymbolsAsync` call:

```csharp
if (SelectedGame == CardGame.Mtg)
{
    var sets = _allSets.Select(s => (s.SetCode, s.SetName)).ToList();
    await setSymbolCache.PreloadSymbolsAsync(sets, progress);
}
```

Avoids pointless 404s against the MTG vectors GitHub repo for One Piece sets. If a One Piece symbol source becomes available later, a second provider can be added.

## Files Affected

| File | Change |
|------|--------|
| `OmniCard/Views/Root/RootViewModel.cs` | Game switch guard, set filter reset, refresh cooldown, symbol preload guard |
| New: `refresh-timestamps.json` (at data path) | Per-game refresh timestamps |

## Testing

- Switch games with pending scans → blocked with message, selection reverts
- Switch games with empty queue → succeeds, set filter cleared
- Refresh card data → succeeds, timestamp recorded
- Refresh again within 24h → blocked with message showing next available time
- Force refresh → bypasses cooldown with confirmation
- Refresh One Piece → no set symbol preload attempted
- Refresh MTG → set symbol preload runs as before
