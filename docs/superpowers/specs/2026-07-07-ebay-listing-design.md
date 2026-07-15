# eBay Listing Integration Design

## Overview

Add the ability to list cards from the collection on eBay, with catalog matching, market-based pricing, full lifecycle tracking, and periodic status sync.

## Data Model

New `EbayListing` entity linked to `CollectionCard`:

| Field                  | Type       | Notes                                      |
|------------------------|------------|--------------------------------------------|
| Id                     | int (PK)   |                                            |
| CollectionCardId       | int (FK)   | Links to `CollectionCard`                  |
| EbayItemId             | string     | eBay's listing ID                          |
| EbayCatalogProductId   | string?    | Matched catalog product                    |
| Status                 | enum       | Draft, Active, Sold, Ended, Error          |
| ListingType            | enum       | FixedPrice, Auction                        |
| ListedPrice            | decimal    |                                            |
| SoldPrice              | decimal?   |                                            |
| StartTime              | DateTime?  |                                            |
| EndTime                | DateTime?  |                                            |
| AuctionDuration        | int?       | Days (1, 3, 5, 7, 10) for auction type    |
| BuyerUsername           | string?    | Populated on sale                          |
| LastSyncedAt           | DateTime?  |                                            |
| CreatedAt              | DateTime   |                                            |
| ErrorMessage           | string?    | Last error from eBay API                   |

`CollectionCard` gets a navigation property `EbayListing?` for grid/editor access.

## Service Layer

### EbayCatalogService

Finds matching eBay catalog products and retrieves market pricing data.

- `SearchCatalogAsync(string cardName, string setName, string? collectorNumber)` — finds matching eBay catalog products
- `GetMarketPriceAsync(string catalogProductId, string condition, bool isFoil)` — returns median/low/high from recent sold and active listings
- Uses eBay Browse API (`/buy/browse/v1/item_summary/search`)

### EbayListingService

Creates and manages listings via eBay Inventory API.

- `CreateListingAsync(CollectionCard card, EbayListingOptions options)` — creates inventory item + offer, publishes listing
- `EndListingAsync(EbayListing listing)` — ends an active listing
- `ReviseListingAsync(EbayListing listing, EbayListingOptions options)` — updates price/details on active listing
- Handles photo upload (scan image + stock image) via eBay Picture Services

### EbaySyncService

Periodic synchronization of listing statuses.

- `SyncAllActiveAsync()` — queries eBay for all tracked active listings, updates to Sold/Ended
- `SyncSingleAsync(EbayListing listing)` — refreshes one listing's status
- Uses eBay Sell Fulfillment API to detect sales and pull buyer info

### EbayListingOptions

Data object passed to create/revise operations:

| Field             | Type    | Notes                                        |
|-------------------|---------|----------------------------------------------|
| ListingType       | enum    | FixedPrice, Auction                          |
| Price             | decimal |                                              |
| AuctionDuration   | int?    | 1, 3, 5, 7, 10 days                         |
| Condition         | string  | Mapped to eBay condition enum                |
| Description       | string  | Auto-generated, user-editable                |
| Title             | string  | Auto-generated, user-editable                |
| IncludeScanImage  | bool    |                                              |
| IncludeStockImage | bool    |                                              |
| ShippingPolicyId  | string  | From seller's eBay account                   |
| ReturnPolicyId    | string  | From seller's eBay account                   |
| PaymentPolicyId   | string  | From seller's eBay account                   |

Shipping, return, and payment policies are pulled from the user's eBay seller account (configured once on eBay, referenced by ID) rather than built from scratch in the app.

## Listing Dialog UI

Modal dialog opened from collection context menu ("List on eBay").

### Left Panel — Card Info (read-only)

- Card image (scan image)
- Name, set, collector number, rarity
- Condition, foil status
- Purchase price (for profit reference)

### Right Panel — Listing Configuration

**Catalog Match section:**
- Auto-matched catalog product with confidence indicator
- "Search" button for manual re-matching
- Market data summary: median sold price, price range, number of recent sales

**Pricing section:**
- Auto-calculated price pre-filled (median sold, adjustable via percentage/offset in settings)
- User can override
- Purchase price shown alongside for profit visibility
- Listing type toggle: Buy It Now / Auction
- Auction duration dropdown (1/3/5/7/10 days) — visible only when Auction selected

**Photos section:**
- Scan image thumbnail (if available) + stock image thumbnail
- Checkboxes to include/exclude each
- Drag to reorder primary image

**Details section:**
- Auto-generated title (e.g., "MTG Black Lotus [LEA] #232 NM") — editable
- Auto-generated description — editable text box
- Condition dropdown mapped to eBay condition values

**Policies section:**
- Dropdowns for shipping/return/payment policies (fetched from eBay seller account)
- Cached after first fetch, refresh button

### Bottom Bar

- "List on eBay" primary button
- "Save as Draft" secondary button
- "Cancel" button

## Collection Integration

### Collection Grid

- New optional "eBay Status" column: Draft, Active, Sold, Ended, Error (color-coded)
- Shows listed price when active
- Hidden by default, toggleable like existing columns

### Card Editor Dialog

New "eBay" section at the bottom:

- Current listing status (or "Not Listed")
- Link to view on eBay (opens browser) when Active
- "List on eBay" button when not listed
- "End Listing" / "Revise" buttons when Active
- Sold price and buyer when Sold

### Collection Context Menu

- "List on eBay" — opens listing dialog (disabled if already Active)
- "View on eBay" — opens eBay listing in browser (visible only when Active/Sold)
- "End eBay Listing" — ends the listing with confirmation (visible only when Active)

### Status Sync

- `DispatcherTimer` fires `SyncAllActiveAsync()` every 5 minutes while the app is running
- Also syncs on app startup and when switching to Collection tab
- Status changes update the grid in real-time via property change notifications
- When a card is detected as Sold, optionally prompt to remove from collection (configurable in settings)

### Collection Filters

- Existing filter/sort builder extended with eBay status filter: All / Not Listed / Active / Sold / Ended

## eBay APIs Used

| API                     | Purpose                              | Scope                              |
|-------------------------|--------------------------------------|------------------------------------|
| Browse API              | Catalog search, market pricing       | `/oauth/api_scope`                 |
| Inventory API           | Create/manage listings               | `/oauth/api_scope/sell.inventory`  |
| Account API             | Fetch seller policies                | `/oauth/api_scope/sell.account`    |
| Sell Fulfillment API    | Detect sales, buyer info             | `/oauth/api_scope/sell.fulfillment`|

All required scopes are already requested in the existing OAuth flow.

## Auth Fixes (completed)

Two bugs fixed as part of this work:

1. **Redirect detection** — Added `AcceptUrl` to `EbaySettings` separate from `RuName`. The WebView now matches against the Accept URL (the actual redirect destination from eBay's developer portal) instead of the RuName identifier.
2. **Scope URLs** — Fixed to always use `https://api.ebay.com` as the scope prefix regardless of sandbox/production environment.
