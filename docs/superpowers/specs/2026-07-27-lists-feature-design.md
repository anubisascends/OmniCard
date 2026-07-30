# Lists Feature — Design

**Date:** 2026-07-27
**Status:** Approved (design), pending implementation plan

## Summary

Add a **Lists** feature: users create named, persisted lists of cards (per game), populate
them by manually adding a specific printing, importing a Moxfield/Archidekt URL (MTG only),
or pasting a plain-text decklist. When a list is populated by paste or URL, the app resolves
each card to its **cheapest non-foil printing** and stores that printing frozen. Users can
run the existing decklist reports (owned-vs-missing / estimated cost, plus PDF export) against
any list at any time.

A list is a *want-list*: it never mutates inventory. Reports diff the list against the
collection exactly like today's "Check Decklist" dialog.

## Goals / Workflow

1. Select a game from the game filter.
2. Open the **Lists** tab.
3. **Create** a new list.
4. **Add a card** by searching and picking a specific printing.
5. **Import** a Moxfield/Archidekt URL to pull a list (MTG only).
6. **Paste** a plain-text decklist (e.g. `decklist.txt`); the app adds each card's cheapest
   non-foil printing.
7. **Run reports** (summary/detailed) against the list on demand.

## Decisions (from brainstorming)

- **Game scope:** All games. Add-a-card and paste work for every game; URL import stays
  MTG-only (Moxfield/Archidekt are MTG sites). Reports and the cheapest lookup are generalized
  to use the list's game instead of hardcoded `CardGame.Mtg`.
- **"Cheapest copy":** cheapest **non-foil** printing by market price across all printings.
- **Manual add:** user picks a **specific printing** from search results (ManualAdd pattern).
- **Printing resolution:** **frozen at add-time**; prices/printings only re-evaluate on an
  explicit **Refresh Prices** action.
- **Cheapest fallback:** if no printing of a card has a price, pick the first printing and mark
  it **unpriced** (flagged in the UI).

## Assumptions (confirmed)

- Multiple named lists per game.
- A list never touches inventory quantities; reports compute owned-vs-missing against the
  collection like the existing decklist check.
- Adding a card already present increments its quantity; paste merges duplicate names (the
  existing parser already dedupes by name).
- Basic lands are added when building a list; the **report** step keeps the existing
  "ignore basic lands" toggle.
- Manual Add allows choosing foil; paste/URL default to cheapest non-foil.
- Report output is a saved PDF via the existing `SaveFileDialog` flow, plus an on-screen
  owned/missing/cost summary.

## Architecture

Follows the existing `Order`/`OrderLine` + `OrderService` + Sales-tab precedents exactly.

### Data model — `OmniCard.Shared\Models\`

**`CardList`**
- `Id` (int, PK)
- `Name` (string)
- `Game` (`CardGame`, stored as string via `HasConversion<string>()`)
- `CreatedUtc` (DateTime)
- `Notes` (string?, nullable)

**`CardListItem`**
- `Id` (int, PK)
- `CardListId` (int, FK → `CardList`, cascade delete)
- `Quantity` (int)
- `GameCardId` (string) — the frozen printing's game-specific id
- `CardName` (string)
- `SetCode` (string?)
- `CollectorNumber` (string?)
- `IsFoil` (bool, default false)
- `AddedMarketPrice` (decimal?, stored as `TEXT`) — price captured at add/refresh time
- `IsUnpriced` (bool) — true when no printing had a price and a fallback printing was chosen
- `Source` (enum `ListItemSource { Manual, Url, Paste }`, stored as string)

### Persistence

- Add `DbSet<CardList> CardLists` and `DbSet<CardListItem> CardListItems` to
  `OmniCardDbContext`, with fluent config blocks (keys, enum `HasConversion<string>()`,
  FK delete behavior, index on `CardListId`).
- Add `CREATE TABLE IF NOT EXISTS CardLists (...)` and `CardListItems (...)` DDL (plus an index
  on `CardListId`) to `UnifiedMigrationService.EnsureUnifiedSchema` so existing `inventory.db`
  files gain the tables. Money columns declared `TEXT`; decimals aggregated client-side.
- No EF migrations folder — this repo manages schema via `EnsureCreated` + `UnifiedMigrationService`.

### Service — `IListService` / `ListService` (`OmniCard.Collection`)

Uses `IDbContextFactory<OmniCardDbContext>` (create-per-operation), injects `ICardService`.

- `IReadOnlyList<CardList> GetLists(CardGame game)`
- `CardList CreateList(string name, CardGame game)`
- `void RenameList(int listId, string name)`
- `void DeleteList(int listId)`
- `IReadOnlyList<CardListItem> GetItems(int listId)`
- `void AddItem(int listId, CardMatch printing, CardGame game, bool isFoil, int quantity)` —
  manual add of a specific printing (increments if the same printing already present).
- `void RemoveItem(int itemId)`
- `void SetQuantity(int itemId, int quantity)`
- `int AddCardsByName(int listId, CardGame game, IEnumerable<DecklistEntry> entries)` —
  cheapest resolver used by paste and URL import. For each entry: enumerate
  `GetGameService(game).GetPrintings(name)`, look up non-foil prices via `GetCurrentPrices`,
  pick the lowest-priced printing; on no-price, pick the first printing and set `IsUnpriced`.
  Merges into existing items by resolved `GameCardId`. Returns count added.
- `void RefreshPrices(int listId)` — re-resolves cheapest printing + price for paste/url items
  and refreshes prices for manual items.
- `List<DecklistEntry> ToDecklistEntries(int listId)` — projects items into `DecklistEntry`
  records so reporting reuses `CheckAgainstCollection` unchanged.

### Reporting — generalize to the list's game

`DecklistService.CheckAgainstCollection` currently hardcodes `CardGame.Mtg` for card-detail and
price lookups. Add a `CardGame` parameter (or overload) so it uses the list's game. The
owned-vs-missing / cost logic is otherwise unchanged. `IDecklistPdfExporter.Export` /
`ExportDetailed` are reused as-is. The `IDecklistService` interface signature is updated
accordingly; the existing Check Decklist dialog passes `CardGame.Mtg` to preserve current behavior.

### UI — new "Lists" sidebar tab

- Append a `TabItem` in `RootView.xaml` **after Sales (index 4)** with a `PackIcon` header
  following the existing pattern, hosting a new `ListsView` UserControl with
  `DataContext="{Binding ViewModel.Lists}"`. Appending avoids shifting the hardcoded tab-index
  literals in `RootViewModel`.
- Add `public ListsViewModel Lists { get; }` to `RootViewModel`, constructor-injected like `Sales`.
- Register `IListService`/`ListService` and `ListsViewModel` as singletons in `App.xaml.cs`;
  register the add-card picker dialog View/VM as transient if a separate dialog is used.
- Add `Lists.SetGame(value)` to `RootViewModel.OnSelectedGameChanged`, alongside
  `Collection.SetGame`, so the list panel filters to the current game.

**`ListsView` layout**
- **Left panel:** the current game's lists + **Create / Rename / Delete** buttons.
- **Right panel (selected list):** items grid (qty, name, set, foil, price, unpriced flag) with:
  - **Add Card** — search picker reusing `GetGameService(game).SearchCards(query)`; user picks a
    specific printing (+ foil, quantity).
  - **Import URL** — Moxfield/Archidekt field, visible/enabled only for MTG lists; calls
    `FetchDecklistAsync` → `AddCardsByName`.
  - **Paste Cards** — textbox parsed by the existing `ParseDecklistText` → `AddCardsByName`.
  - **Refresh Prices**, **Remove**, inline quantity edit.
  - **Summary Report** / **Detailed Report** — `ToDecklistEntries` → `CheckAgainstCollection`
    (list's game) → `IDecklistPdfExporter`, saved via `SaveFileDialog`.
  - On-screen owned/missing/cost summary mirroring the Check Decklist dialog.

### DI registration (`App.xaml.cs`)

- Services block: `services.AddSingleton<IListService, ListService>();`
  `services.AddSingleton<ListsViewModel>();`
- Dialog block (if a separate add-card picker dialog): register View + VM transient.

## Error handling

- **Card not found** (paste/URL name doesn't resolve to any printing): skip the entry, collect
  its name, and surface a status message listing unresolved names (mirrors the decklist parser's
  behavior of silently skipping non-matching lines, but reported).
- **No price for any printing:** fall back to first printing, set `IsUnpriced`, show a flag in the
  grid.
- **URL fetch failure:** same as the existing decklist dialog — status message inviting the user
  to paste instead.
- **Empty list name / duplicate name:** validate on Create/Rename; block empty, allow duplicates
  (name is not a key).

## Testing

- **Unit (`OmniCard.Tests`):**
  - `ListService` cheapest-resolution: given fake printings + prices, picks lowest non-foil;
    fallback sets `IsUnpriced`; merges duplicates by resolved printing.
  - `AddCardsByName` with the `decklist.txt` sample: correct item count and quantity merging.
  - `ToDecklistEntries` round-trips quantities and set codes.
  - CRUD: create/rename/delete cascade behavior (delete list removes items).
  - Reuse existing `DecklistTextParserTests` for parse coverage (unchanged parser).
- **Manual verification:** launch app, create a list per a non-MTG game and an MTG game, add via
  each of the three paths, run both reports. WPF GUI has no automatable UI surface.

## Out of scope

- Editing/overriding a resolved printing after paste/URL add (frozen; user can Remove + Add).
- Sharing/exporting lists to Moxfield/Archidekt.
- Non-MTG URL import.
- Auto-refresh of prices (manual Refresh only).
