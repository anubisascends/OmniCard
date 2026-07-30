# Sales & Fulfillment Module — Design

**Date:** 2026-07-20
**Status:** Approved (pending spec review)
**Author:** Andrew Riebe (with Claude)

## 1. Problem & Goals

The user sells TCG singles on TCGPlayer but is not yet a Level-4 seller, so the
TCGPlayer API is unavailable. They need in-app tooling to run the manual
sell/fulfill workflow and to track sales, customers, orders, and profit.

Concretely:

- Mark cards as **Listed for Sale** (right-click), tracked as a distinct state.
- Generate a **pick list** of listed cards so they can be pulled from storage.
- Mark cards as **Picked** — physically pulled and moved to a designated
  "For Sale" location.
- Record **sales** so sold cards leave inventory but remain in history for
  sales/P&L reporting.
- Track **customers** and link them to **orders**.
- Track **orders** (line items, reference numbers, tracking numbers).
- Print a **receipt / packing slip** on an 80 mm thermal printer
  (width configurable), with a configurable **company profile** (name, address,
  logo).

### Success criteria

- A card can move Available → Listed → Picked → Sold, with each transition
  reflected in inventory location and movement history.
- The pick list shows every Listed-not-Picked card grouped by original location.
- Orders remove sold cards from inventory (at ship time) and feed a net-profit
  P&L that unions manual and eBay sales.
- A receipt prints to the thermal printer at the configured width and exports to
  PDF.

## 2. Architecture Decision (Fork A — Option 1)

Introduce a **channel-agnostic `Listing` entity** for Manual + TCGPlayer.
The existing `EbayListing` entity and eBay sync are left untouched (low risk).
Unification happens at the **Orders + reporting** layer: customers, orders, and
the realized-P&L view roll up across both eBay sales and manual orders.

A `SalesChannel` enum (`Manual`, `TcgPlayer`, `Ebay`) is defined for
forward-compatibility and used on `Listing` and `Order`. eBay listings are not
migrated into `Listing`; instead the P&L/sales views union eBay `Sell` movements
with manual order sales (both already produce `MovementType.Sell` movements, so
the union is natural).

Rejected: full refactor of `EbayListing` into one table (touches working eBay
sync + requires migrating live data; no proportional benefit now).

## 3. Data Model

All new entities live in the unified `OmniCardDbContext` (`inventory.db`),
following existing entity conventions. New enums live in `OmniCard.Shared/Models`.

### Enums

```
enum SalesChannel { Manual, TcgPlayer, Ebay }
enum ListingStatus { Listed, Picked, Sold, Cancelled }
enum OrderStatus { Open, Packed, Shipped, Completed, Cancelled }
```

### `Listing`

| Field         | Type           | Notes |
|---------------|----------------|-------|
| Id            | int            | PK |
| LotId         | int            | FK → InventoryLot |
| Channel       | SalesChannel   | Manual / TcgPlayer |
| Status        | ListingStatus  | Listed → Picked → Sold / Cancelled |
| ListedPrice   | decimal        | asking price |
| Quantity      | int            | number of copies listed (≤ lot quantity) |
| OriginalLocationId | int?      | snapshot of the lot's location at list time (so pick list & unlist can restore/show it) |
| ListedAt      | DateTime       | |
| PickedAt      | DateTime?      | |
| ExternalRef   | string?        | e.g. TCGPlayer listing id, optional |
| OrderLineId   | int?           | set when sold (links to the order line) |
| Note          | string?        | |

Only one active (non-Cancelled, non-Sold) `Listing` per lot at a time.

### `Customer`

| Field | Type | Notes |
|-------|------|-------|
| Id | int | PK |
| Name | string | required |
| Email | string? | |
| Phone | string? | |
| TcgPlayerUsername | string? | |
| AddressLine1 / AddressLine2 | string? | |
| City / State / PostalCode / Country | string? | |
| Notes | string? | |
| CreatedAt | DateTime | |

### `Order`

| Field | Type | Notes |
|-------|------|-------|
| Id | int | PK |
| CustomerId | int | FK → Customer |
| Channel | SalesChannel | |
| OrderNumber | string? | external/marketplace reference number |
| OrderDate | DateTime | |
| Status | OrderStatus | Open → Packed → Shipped → Completed / Cancelled |
| TrackingNumber | string? | |
| Carrier | string? | |
| ShippingChargedToBuyer | decimal | default 0 |
| ShippingCost | decimal | seller's actual postage/supply cost, default 0 |
| MarketplaceFees | decimal | default 0 |
| Notes | string? | |
| CreatedAt | DateTime | |
| ShippedAt | DateTime? | |

### `OrderLine`

| Field | Type | Notes |
|-------|------|-------|
| Id | int | PK |
| OrderId | int | FK → Order |
| LotId | int? | FK → InventoryLot (nullable — lot may be gone later) |
| ProductId | int? | snapshot reference |
| NameSnapshot | string | card name at sale time |
| SetSnapshot | string? | set name/code at sale time |
| ConditionSnapshot | string? | condition at sale time |
| IsFoilSnapshot | bool | |
| Quantity | int | |
| UnitSalePrice | decimal | |

**Snapshots** guarantee receipts and historical P&L remain correct even if the
product/lot is later edited or deleted.

### Settings — Company profile & receipt

Stored as a dedicated JSON file in the data directory (`sales-settings.json`),
mirroring the `collection-presets.json` pattern so the profile travels with the
data directory on migration. (`ForSaleLocationId` lives here too.)

```
CompanyProfile {
  Name, AddressLine1, AddressLine2, City, State, PostalCode, Country,
  Email, Phone, LogoPath (image file copied into the data dir)
}
ReceiptSettings {
  WidthMm (default 80), MarginMm, FontPointSize, ShowPrices (bool),
  FooterText, DefaultPrinterName (optional)
}
SalesSettings {
  ForSaleLocationId (int? — the StorageContainer cards move to on Pick)
}
```

## 4. Lifecycle & Movements (reuse existing movement system)

```
Available
  │  [right-click ▸ List for Sale…]  (choose channel + price + qty)
  ▼
Listed        Listing{Status=Listed}. Card STAYS in its original location.
  │           OriginalLocationId snapshotted. Tile shows a "Listed" badge.
  │  [right-click ▸ Mark Picked]  (or bulk from pick list)
  ▼
Picked        Listing{Status=Picked, PickedAt}. MovementType.Move recorded;
  │           lot.LocationId := ForSaleLocationId.
  │  [added to an Order; order marked Shipped]
  ▼
Sold          For each order line: MovementType.Sell (UnitValue=UnitSalePrice),
              lot.Quantity decremented (→ 0 leaves inventory).
              Listing{Status=Sold, OrderLineId set}.

Unlist (from Listed or Picked) → Listing{Status=Cancelled};
   if Picked, a Move movement returns the lot to OriginalLocationId.
```

- **No new movement types** — `Move` (pick) and `Sell` (sale) already exist.
- Inventory is only removed at **Shipped**, so Open orders are freely editable
  and cancellable without side effects.

## 5. UI

### Collection view (existing) — right-click additions

- **List for Sale…** → dialog: channel (Manual/TCGPlayer), price (prefilled from
  MarketPrice), quantity. Bulk-capable.
- **Unlist** (visible when listed).
- **Mark Picked** (visible when listed, not yet picked). Bulk-capable.
- **"Listed" badge** on tiles (and a subtle "Picked" variant) so on-market cards
  are visible at a glance.

### New "Sales" tab

Sub-views (following existing tab/sub-view patterns):

1. **Pick List** — every Listing with Status=Listed, grouped and sorted by
   original location (Section/Page/Slot), with card name/set/condition/price.
   On-screen + **Print pick list**. Bulk **Mark Picked** from here.
2. **Orders** — list + editor. Create order → select/[create] customer → add
   picked cards (search/add from picked pool) → enter OrderNumber, tracking,
   carrier, fees, shipping charged, shipping cost → set status. **Print Receipt**.
   Marking **Shipped** performs the inventory removal + Sell movements.
3. **Customers** — CRUD list + editor.

### Settings page (Sales)

Company profile (name/address/logo picker), receipt width (mm) + font/margins +
show-prices toggle + footer text, For-Sale location picker, default printer.

## 6. Receipt / Packing Slip (Fork B)

Define the receipt **content model** once
(`ReceiptDocument`: company header, order info, customer block, line items,
totals, footer). Render two ways:

- **Print:** build a WPF `FixedDocument` sized to `WidthMm` (continuous height)
  and print via native `PrintDialog` (user selects the 80 mm thermal printer;
  the driver handles the paper width). On-screen **preview** before print.
- **PDF export:** render the same content via **QuestPDF** using a continuous
  page of `WidthMm` width (mirrors the existing `AuditPdfExporter` pattern,
  `QuestPDF.Settings.License = Community`).

Receipt content: logo + company name/address; order number + date + tracking;
customer name/address; table of cards (name, set, condition, foil, qty, and
optionally price per `ShowPrices`); totals (items, shipping, total) when prices
shown; footer text (e.g. "Thank you!").

## 7. P&L Extension

Extend `AnalyticsService`:

- Realized proceeds/cost continue to come from `MovementType.Sell` movements
  (this already covers both eBay and the new manual sales, since manual sales
  also emit `Sell` movements).
- **Net profit** additionally subtracts order-level `MarketplaceFees` and
  `ShippingCost` and adds `ShippingChargedToBuyer`, attributed by ship date.
- Reporting surfaces both **gross** (proceeds − item cost) and **net**
  (gross − fees − shipping cost + shipping charged).

## 8. Build Phasing

Each phase keeps build + tests green and is independently useful.

1. **Listing & Pick foundation** — enums; `Listing` entity + EF config +
   migration; `SalesSettings.ForSaleLocationId`; right-click List / Unlist /
   Mark Picked (+ bulk); Move-on-pick; "Listed" tile badge; **Sales tab → Pick
   List** view + print. Service + tests.
2. **Customers & Orders & sales** — `Customer`, `Order`, `OrderLine` entities +
   migration; Customers CRUD; Orders create/edit; add picked cards to orders;
   status flow with **Shipped ⇒ remove inventory + Sell movements + snapshot
   lines**; net-profit P&L extension. Service + tests.
3. **Receipt** — `CompanyProfile` + `ReceiptSettings`; settings UI incl. logo;
   `ReceiptDocument` content model; FixedDocument print + PrintDialog + preview;
   QuestPDF PDF export.
4. **TCGPlayer CSV import** — import TCGPlayer order/packing-slip CSV to
   auto-create orders and match line items to lots (fuzzy match on
   name/set/condition), with a review step before commit.

## 9. Resolved Decisions

- **Settings storage:** company profile, receipt settings, and `ForSaleLocationId`
  live in a dedicated `sales-settings.json` in the data directory (see §3),
  matching the `collection-presets.json` convention.
- **Logo storage:** the chosen image is copied into the data directory and stored
  as a relative path, so it travels with a data-dir migration.
- **Partial-quantity listings:** supported via `Listing.Quantity`; the List
  dialog exposes quantity, defaulting to the full lot quantity.

## 10. Out of Scope (for now)

- Live TCGPlayer API integration (blocked by seller level).
- Automatic shipping-label purchase / carrier API.
- Multi-currency.
- Full merge of eBay listings into the unified `Listing` table.

## 11. Testing Strategy

- Service-layer xUnit tests (in-memory SQLite, matching existing
  `CollectionSortFilterTests` harness) for: list/unlist/pick transitions +
  movements; order ship → inventory removal + Sell movements + snapshots;
  net-profit P&L math (fees/shipping); pick-list grouping.
- Receipt rendering: unit-test the `ReceiptDocument` content model assembly;
  smoke-test PDF generation (bytes produced, no throw). Printing is verified by
  human E2E.
- Human E2E for all GUI flows before merge (established workflow).
