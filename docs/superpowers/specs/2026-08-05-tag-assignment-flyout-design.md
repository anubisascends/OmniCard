# Tag Assignment Flyout — Design

**Issue:** [#70 — FEATURE: Make TAG assignment easier](https://github.com/anubisascends/OmniCard/issues/70)

## Background

Issue #70 asks for right-click tag assignment on card lists: a context menu entry opening a
tag selector, alphabetically ordered, supporting multiple tags at once, applying to every card
in a stack, and applying in bulk across a multi-selection.

At the time this issue was filed (2026-08-04 23:54 UTC), the base tagging system
([[tagging-system]] design, commit `c4ac889`, merged to `master` at 2026-08-04 14:04 UTC —
*before* the issue was opened) already delivered most of this literally:

- Right-click → **"Add Tag(s)..."** on the Collection card list (`CardListView.xaml`)
- A dialog (`AddTagsView`) for entering tags
- `TagService.GetAllTags()` ordered alphabetically
- Multiple tags addable in one dialog use
- Stacks already expanded via `GetAllSelectedCardIds()` → `StackedIds`
- Multi-selection bulk add already wired (`CollectionViewModel.AddTagsToSelected`)

The gap is the **interaction model**: today's picker (`OmniCard.Controls/TagEditor`) is a
type-to-add autocomplete chip box — nothing is shown until you start typing. Issue #70's
wording ("select a tag from the list of tags available") describes browsing/clicking a visible
list, not filtering by typing. This design replaces that interaction, and extends tag
assignment to card lists that don't have it today (Scanner review, and Locations by way of
reusing `CardListView`).

## Decisions (confirmed with the reporter)

- **Interaction:** a right-click **"Tags ▸"** entry opens a flyout listing every existing tag as
  checkable rows — not the current type-first autocomplete, and not a centered modal dialog.
- **Toggle, not additive-only:** the flyout reflects each tag's current state on the selection
  (checked / unchecked / indeterminate) and clicking toggles it — add and remove through the
  same gesture.
- **Scope:** Collection view, Locations (drills into the same `CardListView`), and the Scanner
  review list — every place cards are listed gets the same flyout.
- **New tags:** a "+ New Tag..." row at the top of the flyout creates and immediately applies a
  tag inline, no separate dialog.
- **Mixed selections:** a tag present on some but not all selected cards renders indeterminate;
  clicking it applies to the whole selection (click again to remove from all).
- **Large tag lists:** the flyout includes a live filter box, since a plain scrollable list
  degrades once a collection has many tags.
- **Build approach:** a custom `Popup`-based flyout `UserControl`, not a native WPF checkable
  `MenuItem` submenu. WPF's `MenuItem IsCheckable` + `StaysOpenOnClick` combination is fragile
  (auto-closes on click unless carefully suppressed) and a `TextBox` embedded inside a
  `MenuItem` fights the menu's own keyboard/arrow-key navigation for focus. A dedicated control
  avoids both problems and is architecturally close to what `AddTagsView` already does, just
  anchored as a flyout instead of a centered modal window.

## Architecture

### New control: `OmniCard.Controls/TagFlyout.xaml(.cs)`

A `Popup`-anchored `UserControl`, opened when the "Tags ▸" context menu item is clicked
(positioned like a submenu, adjacent to the parent menu item). Contains:

- A filter `TextBox` pinned at the top (live, case-insensitive substring match — same matching
  style as the existing `TagEditor` autocomplete).
- A "+ New Tag..." row above the tag list. Clicking swaps it for an inline text box; Enter
  commits.
- A scrollable list of checkable rows, one per tag (post-filter), each in one of three states:
  `Checked`, `Unchecked`, `Indeterminate` (partial-check glyph for "present on some, not all, of
  the selection").

Exposed surface: `AllTags` (name + check-state), bound `Filter` text, and
`TagToggled(name, newState)` / `NewTagCreated(name)` callbacks/commands. The control has no
knowledge of `InventoryLot` vs `ScannedCard` — it only deals in tag names and check-states: the
consuming ViewModel supplies the list and reacts to toggles.

### Service layer

`ITagService` gains one method:

```csharp
/// <summary>Removes the tag from every listed lot (does not delete the tag itself, even if its
/// usage count drops to zero — mirrors AddTagToLots).</summary>
void RemoveTagFromLots(IEnumerable<int> lotIds, string tagName);
```

Implemented in `TagService` alongside the existing `AddTagToLots`, deleting the matching
`LotTag` rows for the given lot ids. The existing `GetTagsByLots(IEnumerable<int>)` (already
batch-shaped) is what computes each tag's check-state when the flyout opens: a tag is `Checked`
if every selected lot has it, `Unchecked` if none do, `Indeterminate` otherwise.

A shared helper computes this tri-state list from `GetAllTags()` + `GetTagsByLots(selectedIds)`
so Collection, Locations, and Scanner build the flyout's contents identically instead of each
view re-deriving the same logic.

### Per-surface wiring

**Collection / Locations** (`CollectionViewModel`, `CardListView.xaml`): "Tags ▸" replaces the
existing "Add Tag(s)..." menu item. `GetAllSelectedCardIds()` (already expands `StackedIds`)
feeds the flyout. Each toggle calls `AddTagToLots` / `RemoveTagFromLots` immediately (one DB
write per toggle, same transactional shape as today's bulk-add), then refreshes the visible tag
badges on affected rows without a full `SearchCollection()` re-run.

**Scanner review list** (`ScannerTabView.xaml`, pre-commit `ScannedCard` rows): no DB write —
toggling mutates `ScannedCard.Tags` in memory, the same collection the detail panel's
`TagEditor` already edits, so right-click and detail-panel edits stay consistent and both flow
through the existing `CommitScans` tag-writing path on commit. The tag *names* offered still
come from `ITagService.GetAllTags()` (persisted tags — you can apply an existing tag to a new
scan before it has a lot id), but check-state is computed from the in-memory
`ScannedCard.Tags` of the selected rows rather than `GetTagsByLots`.

### Retired

`AddTagsView` / `AddTagsViewModel` (the modal chip-entry dialog) and its "Add Tag(s)..." menu
item are removed — the flyout covers add, remove, and multi-tag entry in one place, so keeping
both would be a redundant second entry point for the same job. `TagEditor` (the chip-style
type-to-add control) is unaffected: it's still used for the single-card
`CollectionCardEditor` and `ScannerDetailPanelView` tag fields, which aren't part of this issue.

## Out of scope

- Changing how tags are edited in the single-card editor (`CollectionCardEditor`) or scan
  detail panel — those keep the existing `TagEditor` chip control.
- `ManageTagsView` (rename/merge/delete) is unaffected.
- Web companion — tags stay read-only there; no assignment UI is being added to
  `OmniCard.Web`.

## Testing

- `TagServiceTests`: `RemoveTagFromLots` — removes join rows; leaves the `Tag` row intact even
  at zero remaining usages; no-op when a lot doesn't have the tag.
- A unit test for the tri-state helper: all-selected-have-tag → `Checked`, none → `Unchecked`,
  some → `Indeterminate`.
- `CollectionViewModel` tests covering the flyout's toggle path (add and remove) replace the
  existing `AddTagsToSelected`/`PickTags` dialog-based tests.
- A Scanner-side test verifying an in-memory toggle on `ScannedCard.Tags` survives
  `CommitScans` and matches what the detail panel would have produced.
- Manual GUI smoke (per this repo's pattern for WPF-only changes — see
  [[async-vm-test-determinism]] sibling notes on why these aren't automated): right-click a
  single card, a stack, and a multi-selection in Collection, Locations, and Scanner; verify
  toggle / indeterminate / new-tag / filter behavior, and that Scanner-side tags survive a
  commit.
