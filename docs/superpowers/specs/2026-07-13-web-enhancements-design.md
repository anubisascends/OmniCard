# OmniCard.Web Enhancements Design

## Overview

Four enhancements to the OmniCard.Web companion app: card stacking, game filtering, collection search, and decklist checking against the collection.

## Architecture Change

Add project references from `OmniCard.Web` to `OmniCard.Collection` and `OmniCard.CardMatching`. Register `ScryfallDbContext`, `OptcgDbContext`, game services (`ScryfallService`, `OptcgService`), and `DecklistService` at startup. All database contexts open in read-only mode since the web app never writes. The existing `DataDirectory` configuration already points at the folder containing all `.db` files.

## Feature 1: Card Stacking on Location Page

**Page**: `/location/{id}`

Group cards by **Name + SetCode**. Display one row per group with a `Qty` column showing the count. The first card in each group provides the detail link and image reference.

**Query approach**: `GroupBy(c => new { c.Name, c.SetCode })` on the `Cards` table filtered by `ContainerId`. Select the first card's `Id` for linking, and `Count()` for quantity.

**Display**: Add a `Qty` column to the existing cards table. When Qty > 1, show the count. Card name links to `/card/{firstCardId}`.

## Feature 2: Game Filter on Index Page

**Page**: `/` (Index)

Add a game dropdown at the top of the page with options: `All Games`, `Magic: The Gathering`, `One Piece`. Submits as a query parameter (`?game=mtg` or `?game=optcg`).

When a game filter is active:
- Container card counts reflect only cards of that game
- Containers with 0 cards for the selected game are hidden
- The filter combines with search when both are active

The `CollectionCard.Game` field (an enum: `CardGame.Mtg`, `CardGame.OnePiece`) drives the filter.

Game filter is scoped to the Index page only. Location and Card detail pages are unaffected.

## Feature 3: Collection Search on Index Page

**Page**: `/` (Index)

Add a search bar at the top of the Index page (above the container list). Submits as `?q=<query>` via GET. When a query is present:
- The container list is replaced with a card results table
- Results come from querying `CollectionCard` records across all containers
- Results are stacked by Name + SetCode with a Qty column

**Search syntax**: Support the same prefix-based syntax used in the desktop app:
- Plain text: filter by `Name LIKE '%text%'`
- `set:XXX`: filter by `SetCode`
- `cn:XXX`: filter by `CollectorNumber`
- `rarity:XXX`: filter by `Rarity`
- `color:XXX`: filter by `Color`
- `type:XXX`: filter by `CardType`

The search is implemented directly against `CollectionDbContext.Cards` using LIKE queries, not via the game services' `SearchCards` methods (which query different databases). This keeps it simple and avoids needing game-specific routing.

Combines with game filter if active (`?q=lightning&game=mtg`).

## Feature 4: Decklist Check Page

**Page**: `/decklist` (new Razor page)

### Input
A form with a single text input for a Moxfield or Archidekt deck URL. Submits via POST.

### Processing
1. Call `DecklistService.FetchDecklistAsync(url)` to fetch and parse the decklist
2. Call `DecklistService.CheckAgainstCollection(deckName, deckSource, entries)` to compare against the collection
3. Render the `DecklistCheckResult` as HTML

### Output (HTML report)
- **Header**: Deck name, source (Moxfield/Archidekt), total cards, owned count, missing count, estimated cost
- **Owned section**: Cards grouped by type category (Creature, Instant, Sorcery, etc.). Each row: card name, qty needed, locations where copies are found (container name, page/slot if applicable)
- **Missing section**: Cards grouped by type category. Each row: card name, qty needed, market price per card, total cost for that card

No PDF export. The rendered HTML page is the deliverable.

### Error handling
- Invalid URL format: show inline error message
- Network failure fetching deck: show error message
- Empty decklist: show "No cards found in decklist" message

## Files Changed

| File | Change |
|------|--------|
| `OmniCard.Web/OmniCard.Web.csproj` | Add project references to Collection and CardMatching |
| `OmniCard.Web/Program.cs` | Register game DB contexts, game services, DecklistService, ICardService |
| `OmniCard.Web/Pages/Index.cshtml` | Add game filter dropdown, search bar, conditional card results view |
| `OmniCard.Web/Pages/Index.cshtml.cs` | Add search/filter query handling, stacked card result model |
| `OmniCard.Web/Pages/Location.cshtml` | Add Qty column, update table to show grouped cards |
| `OmniCard.Web/Pages/Location.cshtml.cs` | Group cards by Name+SetCode, return stacked results |
| `OmniCard.Web/Pages/Decklist.cshtml` | New page: URL input form + HTML report |
| `OmniCard.Web/Pages/Decklist.cshtml.cs` | New page model: fetch deck, check collection, build view model |
| `OmniCard.Web/wwwroot/css/site.css` | Styles for new elements (search bar, game filter, decklist report) |

## Out of Scope

- PDF export from the web app
- Write operations (the web app remains read-only)
- Game filter persistence across pages
- Search on Location pages
- Card image thumbnails in search results (link to card detail page instead)
