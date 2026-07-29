# eBay Badge + Auto-End on Cross-Channel Sale — Design

**Date:** 2026-07-28
**Status:** Approved
**Branch:** `feat/ebay-seller-setup`

## Problem
The user lists cards on multiple channels. Two gaps:
1. Cards with an active eBay listing are visually indistinguishable from cards listed elsewhere — the tile badge shows a generic "LISTED" for every channel.
2. When a card sells on another channel, its active eBay listing stays live (double-sale risk); it must be ended automatically.

## Decisions (from brainstorming)
- **Badge:** the eBay indicator **replaces** the generic badge — a card with an active eBay listing shows **"eBAY"**; otherwise the existing **LISTED/PICKED**; hidden when neither. Distinct accent color (a blue — deliberately not eBay's multicolor brand mark).
- **Auto-end:** **automatic, best-effort, flag on failure.** On order fulfillment, if a sold lot has an active eBay listing, end it. Failure never blocks the sale; it logs and flags the eBay listing for manual ending.

## Part 1 — "eBAY" badge
- **Signal:** `CollectionCard.EbayListing?.Status == EbayListingStatus.Active` (already batch-loaded via `CardService.AttachEbayListings`).
- **View:** `OmniCard/Views/Root/CardListView.xaml` badge currently binds `ListingStatus` through `ListingStatusToBadgeConverter`. Replace with a `MultiBinding` over (`ListingStatus`, `EbayListing.Status`) → new `ListingBadgeConverter : IMultiValueConverter`:
  - active eBay listing → `"eBAY"`
  - else `ListingStatus == Picked` → `"PICKED"`
  - else `ListingStatus == Listed` → `"LISTED"`
  - else `""` (badge hidden via existing empty/null-to-collapsed visibility).
- **Color:** eBAY badge gets a distinct background (blue accent). Implemented via a second `IMultiValueConverter` (`ListingBadgeBrushConverter`) or a style trigger on the badge text; keep the existing brush for LISTED/PICKED. Explicit `Foreground` (dark-theme rule).
- `ListingStatusToBadgeConverter` may remain for any other callers; the tile switches to the new multi-converter.

## Part 2 — Auto-end on sale
- **Hook:** `OmniCard.Collection/OrderService.cs` at the fulfillment site that calls `listingService.MarkSold(line.LotId, line.Id)` (~line 140). After marking sold, for each sold lot look up an active `EbayListing` (via the db context already in scope) and, if present, call `IEbayListingService.EndListingAsync(listing)`.
- **Best-effort:** wrap the end in try/catch. Success → `EndListingAsync` sets `Ended` (+ `Unlist`). Failure → catch, log, set `EbayListing.ErrorMessage` (keep `Status = Active` so it stays flagged/discoverable) and continue; the sale/fulfillment completes regardless.
- **Idempotent/safe:** ending an already-eBay-sold listing is fine — `EndListingAsync` treats 404 as success.
- **Wiring:** `OrderService` gains an `IEbayListingService` constructor dependency. That interface is in `OmniCard.Shared`, so no new project reference / cycle; DI supplies the `OmniCard.eBay` implementation. If the fulfillment method is currently synchronous, make the relevant path async to `await EndListingAsync` (avoid fire-and-forget, per this repo's async-test-determinism guidance).

## Testing
- `ListingBadgeConverterTests`: active eBay → "eBAY"; picked (no eBay) → "PICKED"; listed (no eBay) → "LISTED"; nothing → "".
- `OrderService` fulfillment tests: sold lot with active `EbayListing` → `EndListingAsync` invoked; end-fails path → fulfillment still completes and `EbayListing.ErrorMessage` is set.

## Non-goals
- Per-channel badges. Preserving PICKED distinction for eBay cards (eBAY wins). Ending listings on other channels' sites (only eBay is API-integrated).
