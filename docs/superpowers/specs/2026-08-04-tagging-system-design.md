# Card Tagging System — Design

## Context

The user wants free-form custom tags on cards ("to trade", "for Commander deck", etc.), with a
tag library to browse/manage them, and `tag:foo` / `tag:foo OR tag:bar` search syntax in the
collection search box. Gaps to fill: how to tag at scan time vs. on already-collected cards, and
whether tags are per-physical-copy or per-print.

## Decisions

- **Per physical copy** (`InventoryLot`), not per print — consistent with how Condition/Foil/
  Location already work per-copy.
- **Full tag-library management** (rename, delete, merge duplicates, usage counts), not just a
  read-only list.
- **Tag editing everywhere a card can be touched**: at scan-review time, on an already-collected
  card via the existing editor, and in bulk across a multi-selection.
- **Web app shows tags read-only** and supports `tag:foo` in its own (simpler, AND-only) search —
  no editing from the web, matching its read-only-DB rule.

## Data model

New tables via `UnifiedMigrationService.EnsureUnifiedSchema` (this app patches schema by hand,
not EF migrations — see existing `Trades`/`EbayListings` for the pattern):
- `Tags(Id, Name, CreatedAt)` — unique index on `Name` (case-insensitive).
- `LotTags(Id, LotId, TagId)` — join table, unique index on `(LotId, TagId)`.

## Search syntax (desktop)

`ScryfallQueryParser` already handles `OR`/parentheses/negation generically for any `field:value`
token — no parser changes needed. `CardService.BuildFieldExpression` gets a new `"tag"` case:
resolves matching `LotId`s from `LotTags` (case-insensitive contains/exact per the existing
`ComparisonOp`), then bakes the result into the expression tree as `HashSet<int>.Contains(c.Id)` —
same shape as every other field-filter builder, evaluated eagerly as a small subquery (mirrors the
existing `ChunkedByIdLookup` "resolve ids ahead of time" precedent used elsewhere for SQLite's
parameter-count limits). `BuildFieldExpression`/`BuildFilterExpression`/`ApplyScryfallFilter` need
`OmniCardDbContext` threaded through — currently `BuildFilteredQuery` already has it, just doesn't
pass it down.

## Services

New `ITagService`/`TagService` (`OmniCard.Collection`):
```csharp
List<TagSummary> GetAllTags();               // Name + usage count
List<string> GetTagsForLot(int lotId);
void SetTagsForLot(int lotId, IEnumerable<string> tagNames);  // replace-all, create-or-reuse by name
void AddTagToLots(IEnumerable<int> lotIds, string tagName);   // bulk add
void RenameTag(int tagId, string newName);
void DeleteTag(int tagId);
void MergeTags(int sourceTagId, int targetTagId);
```

## UI

- One reusable tag-chip editor control (type-to-add with autocomplete from `GetAllTags()`, × to
  remove) used in both the scan-review detail panel and the existing collection-card editor
  dialog.
- New bulk **"Add Tag(s)…"** command alongside the existing Selection-menu bulk actions (Set
  Condition/Set Foil), per this session's "expose every command outside a context menu too" rule.
- New **"Manage Tags…"** dialog (mirrors Manage Storage Locations) off the `_Collection` menu:
  rename/delete/merge with usage counts.
- Collection tiles get a compact tag indicator (icon + count, full list in a tooltip) rather than
  a full chip row — the tile is already busy with the eBay/traded badges and price/qty.
- Committing a scan with tags attaches them to the new lot (`CardService.CommitScans`).

## Web app

- `CollectionCard.Tags` populated via a separate pass after the base DTO is built, same pattern
  already used for `ListingStatus`/live `MarketPrice` (not baked into `CollectionCardMapper.ToDto`
  itself, which has no DB access).
- `Index.cshtml.cs`'s search gets a `tag:foo` term handler alongside its existing `set:`/`rarity:`/
  `color:`/`type:` handlers — simple `AND`-only, matching that search's existing (simpler than
  desktop's) style.
- Tags shown on card tiles/detail pages, read-only.

## Out of scope

- Tag colors (mentioned as a possible nice-to-have, not requested — can add later without schema
  changes beyond one nullable column).
- Web-side tag editing (would need the same file-drop pattern as trades; not requested).

## Verification

- Unit tests for `TagService` (CRUD, merge, rename cascades correctly), the new `tag:` field
  builder in `CardService` (Contains/Exact/negation/OR across two tags), and `CommitScans`
  attaching tags from a scan.
- `dotnet build OmniCard.slnx` and `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`.
- Manual smoke: tag a card while scanning, tag an existing card via the editor, bulk-tag a
  selection, search `tag:foo OR tag:bar` in the desktop search box, rename/merge/delete tags in
  the manage dialog, and confirm the web app shows/searches tags.
