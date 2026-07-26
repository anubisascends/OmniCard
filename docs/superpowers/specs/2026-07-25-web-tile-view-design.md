# Web App Tile View for Search Results

**Date:** 2026-07-25
**Status:** Approved (design)

## Summary

Replace the search-results `<table>` in the OmniCard.Web companion app with a
visual **tile grid**, mirroring the desktop app's collection tile view
([CardListView.xaml](../../../OmniCard/Views/Root/CardListView.xaml)). Each tile
shows card art, name, set name (code), price (when available), and quantity.

Scope is limited to the **search-results view** on the Index page. The default
(non-search) storage-location view is untouched.

## Decisions

- **Replace, not toggle** — search results always render as tiles; the sortable
  results table is removed.
- **Price** — show `Product.LastMarketPrice` when present; omit otherwise.
- **Missing art** — render a "No Image" placeholder box (matching the desktop
  tile) so tile heights stay uniform.
- **Approach** — server-rendered Razor tiles (no new JSON API, no client-side
  rendering framework). Fits the app's existing all-server Razor Pages pattern.

## Current State

- [Index.cshtml](../../../OmniCard.Web/Pages/Index.cshtml) renders search results
  as a `<table>` (Qty, Name, Set, #, Rarity, Color) at lines 50–74.
- [Index.cshtml.cs](../../../OmniCard.Web/Pages/Index.cshtml.cs) `ExecuteSearch()`
  materializes full `CollectionCard` DTOs (via `CollectionCardMapper.ToDto`, with
  `marketPrice` hardcoded to `0m`), then groups by `{ Name, SetCode }` into the
  lightweight `CardSearchResult` record (Id, Name, SetCode, Number, Rarity,
  Color, Quantity). Image, set name, and price are dropped.
- Image URLs are resolvable: [Card.cshtml.cs:95-107](../../../OmniCard.Web/Pages/Card.cshtml.cs)
  resolves `ScanImagePath` → `/scans/{file}` (served statically, see
  [Program.cs:88-96](../../../OmniCard.Web/Program.cs)) else the remote
  `Product.ImageUri`.
- Price field: `Product.LastMarketPrice` (nullable decimal, persisted).
  `Product.MarketPrice` / `CollectionCard.MarketPrice` are `[NotMapped]`
  runtime-only values.

## Design

### 1. Data layer — enrich the search projection

In `Index.cshtml.cs` `ExecuteSearch()`:

- Pass `l.Product.LastMarketPrice ?? 0m` into `CollectionCardMapper.ToDto(...)`
  instead of the hardcoded `0m`, so each materialized DTO carries a real price.
- Keep grouping by `{ Name, SetCode }`. For each group, select a **representative
  card** = the group member with the lowest `Id`. The representative supplies
  `SetName`, image source (`ScanImagePath` / `ImageUri`), and price.
  `Quantity` remains `g.Count()`. Existing scalar fields (Number, Rarity, Color)
  keep their current `g.Min(...)` aggregation.
- Extend the `CardSearchResult` record with:
  - `string SetName` (default `""`)
  - `string? ImageUrl`
  - `decimal? MarketPrice` — `null` when the representative's price is `0`/unset,
    so the view can omit the price line.

### 2. Shared image-URL resolver

Extract the inline image-URL logic from `CardModel.ImageUrl` into a static helper
in `OmniCard.Web`, e.g.:

```csharp
public static class CardImageUrl
{
    public static string? Resolve(string? scanImagePath, string? imageUri)
    {
        if (!string.IsNullOrEmpty(scanImagePath))
            return "/scans/" + Path.GetFileName(scanImagePath);
        return string.IsNullOrEmpty(imageUri) ? null : imageUri;
    }
}
```

Both `CardModel.ImageUrl` and `IndexModel.ExecuteSearch` call it. Single source
of truth; unit-testable.

### 3. View — tile grid replaces the table

In `Index.cshtml`, replace the search-results `<table>` (lines 50–74) with a tile
grid. Each tile is an `<a href="/card/@r.Id">` (preserving today's click-through
to the detail page), containing top to bottom:

- **Art** — `<img src="@r.ImageUrl" loading="lazy" alt="@r.Name">` when
  `ImageUrl` is set; otherwise a `.no-image` placeholder box with "No Image".
  Kept at the 63:88 card aspect ratio.
- **Name** — bold, single line, ellipsis on overflow.
- **Set name (code)** — muted.
- **Price** — `@r.MarketPrice?.ToString("C")`, rendered only when non-null.
- **Qty** — `×@r.Quantity`, rendered only when `> 1`.

The "Search Results (N)" heading and the empty-state message ("No cards found.")
are preserved. `table-sort.js` stays loaded (the storage-location tables in the
default view still use it); it simply has no results table to act on. The
non-search storage-location view is unchanged.

### 4. CSS

Add to [site.css](../../../OmniCard.Web/wwwroot/css/site.css), reusing existing
CSS variables (`--surface`, `--border`, `--text-muted`, `--text`, `--link`):

- `.card-tiles` — `display:grid; grid-template-columns:repeat(auto-fill,minmax(150px,1fr)); gap:12px;`
  (≈4 columns at the 800px body width; collapses to 1–2 on mobile).
- `.card-tile` — surface background, border, radius, padding, hover lift;
  no text-underline on hover.
- `.card-tile img` / `.card-tile .no-image` — `aspect-ratio:63/88; width:100%; object-fit:contain;`
  placeholder centers muted italic "No Image".
- `.card-tile .name / .set / .price / .qty` — sizing + muted/accent colors
  matching the existing palette.

Global `body { max-width:800px }` stays as-is (layout consistent site-wide).

### 5. Testing

- Unit-test `CardImageUrl.Resolve` for its three branches:
  scan path → `/scans/<file>`; remote URI passthrough; null/empty → null.
- If an existing web test harness is available, add a projection test asserting
  quantity grouping and representative price/image selection. Confirm harness
  availability during planning; do not add a new test project solely for this.

## Out of Scope

- Live/market price refresh (uses the already-persisted `LastMarketPrice`).
- Tile/table toggle (explicitly replaced).
- Changes to the storage-location default view, the Card detail page (beyond the
  helper extraction), or any new API endpoints.

## Files Touched

- `OmniCard.Web/Pages/Index.cshtml.cs` — projection enrichment.
- `OmniCard.Web/Pages/Index.cshtml` — table → tile grid.
- `OmniCard.Web/Pages/Card.cshtml.cs` — use shared resolver.
- `OmniCard.Web/` — new `CardImageUrl` helper (+ its unit test).
- `OmniCard.Web/wwwroot/css/site.css` — tile styles.
