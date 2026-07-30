# Sales & Fulfillment — Phase 4 (TCGPlayer Order Import) — Design

**Date:** 2026-07-21
**Status:** Approved (pending spec review)
**Author:** Andrew Riebe (with Claude)
**Parent spec:** [2026-07-20-sales-fulfillment-design.md](2026-07-20-sales-fulfillment-design.md) (§8.4)

## 1. Problem & Goals

Phases 1–3 (merged) deliver listings/pick-list, customers/orders/ship→P&L, and
receipts + a Settings dialog. Phase 4 removes the tedious hand-entry of customer
and order data when fulfilling TCGPlayer sales.

The only order file the seller can download from TCGPlayer is the **Shipping
Export** — **one row per order, with no card-level lines**. Its columns:

```
Order #, FirstName, LastName, Address1, Address2, City, State, PostalCode,
Country, Order Date, Product Weight, Shipping Method, Item Count,
Value Of Products, Shipping Fee Paid, Tracking #, Carrier
```

Because the file has no line items, card-level matching (the parent spec's
original §8.4 idea) is **not possible** and is explicitly out of scope. Phase 4
is therefore **bulk order/customer intake**: import the Shipping Export to
auto-create customers and order shells; the seller adds the cards by hand
afterward (as today) and Ships to record the sale.

### Success criteria

- Importing a Shipping Export creates one `Order` (channel TCGPlayer, status Open)
  per new row, with a matched-or-created `Customer`, order number, date, and
  shipping fee, behind a review step.
- Re-importing the same or an overlapping file creates **no duplicates**
  (existing order numbers are skipped; repeat buyers reuse their `Customer`).
- While adding cards to an imported order, a live hint shows progress toward the
  imported item count and product value, so mis-picks are obvious.

## 2. Scope Decisions (resolved in brainstorming)

- **Model:** create/match `Customer` + create `Order` (order-level fields only);
  cards added manually afterward; orders created as **Open**.
- **Customer match:** on **name + postal code** (case-insensitive); on a match,
  refresh the stored address from the CSV; otherwise create a new `Customer`.
- **Duplicate orders:** a row whose `Order #` already exists is shown as
  "already imported" and excluded from the commit (idempotent re-import; never
  disturbs cards already added to the existing order).
- **Entry point:** an "Import from TCGPlayer CSV…" button on the Sales ▸ Orders
  view.
- **Reconciliation:** capture `Item Count` and `Value Of Products` on the order
  and show a live "added N of M · $X of $Y" hint in the order editor.

## 3. Non-Goals (Phase 4)

- Card-level line matching (no line items in the file) — card-picking stays manual.
- `MarketplaceFees` and seller `ShippingCost` — not in the file; left at 0 for
  manual entry. (A configurable default TCGPlayer fee % is a possible later
  enhancement, not built now.)
- Updating tracking/carrier on re-import (existing orders are skipped, not updated).
- Any change to eBay sync or the receipt/P&L features.

## 4. Data Model

Add two nullable columns to the existing `Order` entity (`OmniCard.Shared/Models/Order.cs`):

| Field | Type | Notes |
|-------|------|-------|
| ImportedItemCount | int? | `Item Count` from the CSV; null for non-imported orders |
| ImportedProductValue | decimal? | `Value Of Products` from the CSV (buyer-paid product subtotal) |

Schema wiring follows the established pattern: register the columns in
`OmniCardDbContext` (`OnModelCreating` needs nothing special for scalar props),
add `ALTER TABLE Orders ADD COLUMN` statements to `EnsureUnifiedSchema`
(decimal → TEXT, int → INTEGER, matching existing convention), and extend the
`UnifiedMigrationService` pre-existing-DB column guard/test. Existing rows read
back as null.

No new enum values (channel `TcgPlayer` and status `Open` already exist).

## 5. Import Service (the tested seam)

New `TcgPlayerOrderImportService` in `OmniCard.Collection`
(`ITcgPlayerOrderImportService` in `OmniCard.Interfaces`), using the CsvHelper
package already referenced by `CsvExportImportService`, over
`IDbContextFactory<OmniCardDbContext>`.

### Preview model (`OmniCard.Shared/Models`)
```csharp
class TcgOrderImportPreview
{
    List<TcgOrderImportRow> Rows;   // one per CSV data row
}

class TcgOrderImportRow
{
    // parsed
    string  OrderNumber;
    string  CustomerName;           // "First Last" (trimmed; handles missing last)
    string? AddressLine1, AddressLine2, City, State, PostalCode, Country;
    DateTime OrderDate;
    decimal ShippingFeePaid;        // → Order.ShippingChargedToBuyer
    int     ItemCount;              // → Order.ImportedItemCount
    decimal ValueOfProducts;        // → Order.ImportedProductValue
    string? TrackingNumber, Carrier;
    // computed against current DB
    int?    MatchedCustomerId;      // null ⇒ will create
    bool    IsNewCustomer;
    bool    IsDuplicateOrder;       // Order # already exists ⇒ skip
    bool    Include;                // default: true, except false for duplicates
}
```

### API
- `TcgOrderImportPreview PreviewImport(string filePath)` — parse (invariant
  culture for numbers/dates), then for each row resolve customer match
  (name+postal against `Customers`) and duplicate-order status
  (`OrderNumber` against `Orders`).
- `int Commit(TcgOrderImportPreview preview)` — for each included, non-duplicate
  row: upsert the `Customer` (create, or refresh address on match) and create the
  `Order` (`Channel=TcgPlayer`, `Status=Open`, `OrderDate`,
  `ShippingChargedToBuyer`, `TrackingNumber`, `Carrier`, `ImportedItemCount`,
  `ImportedProductValue`, `CreatedAt=UtcNow`). Idempotent: a row whose order
  number now exists is skipped. Returns the number of orders created.

  Two within-a-single-commit cases the implementation must handle (the file can
  contain more than one order for the same buyer, and re-running is expected):
  - **Repeat buyer in the same file:** after creating a customer, a later row
    with the same name+postal reuses it (match against customers already created
    in this commit, not only those pre-existing at `PreviewImport` time).
  - **Order existence re-checked at commit time**, not read from the row's
    `IsDuplicateOrder` flag (which was computed at preview time and may be stale),
    so committing the same preview twice creates nothing the second time.

Parsing notes: map columns by header name (tolerant of column order); `Order
Date` parsed as `yyyy-MM-dd`; numeric fields via `decimal.Parse`/`int.Parse`
with `CultureInfo.InvariantCulture`; empty `Address2`/`Tracking`/`Carrier` → null.

## 6. UI

### Orders view (Sales tab)
- **Button** "Import from TCGPlayer CSV…" next to New Order →
  `OrdersViewModel.ImportTcgPlayerCommand` → `OpenFileDialog` (CSV filter) →
  `PreviewImport` → `IDialogService.ShowTcgOrderImportPreview(preview)` → on
  confirm, `Commit` → reload orders + status message ("Imported N orders.").
- **Reconciliation hint** in the editor: `OrdersViewModel` exposes the selected
  order's `ImportedItemCount`/`ImportedProductValue`; a computed line shows
  `added {sum of line quantities} of {ImportedItemCount} items · {OrderTotal:C}
  of {ImportedProductValue:C}`. Hidden when both imported fields are null.

### Preview dialog
- `TcgOrderImportView` + `TcgOrderImportViewModel` (modeled on the existing
  `CsvImportView`/`ShowImportPreview` pattern; MaterialDesign-themed, owned via
  `SetOwner`). A read-only-ish DataGrid of rows: Order #, buyer, city/state,
  date, items, product value, shipping fee, a **status** column (New customer /
  Matched · New order / **Already imported**), and an **Include** checkbox
  (duplicates default unchecked and disabled). Import + Cancel buttons; Import
  returns the count committed.
- `IDialogService.ShowTcgOrderImportPreview(TcgOrderImportPreview preview) → int`
  (count committed), mirroring `ShowImportPreview`.

## 7. Testing Strategy

- **`TcgPlayerOrderImportService`** (xUnit, in-memory SQLite, temp CSV file):
  - Parse the exact Shipping Export header layout → correct field mapping
    (name join, address, `yyyy-MM-dd` date, fee→charged, item count, value).
  - Customer match: existing name+postal → `MatchedCustomerId` set, `IsNewCustomer`
    false; no match → new. Address refreshed on match.
  - Duplicate order: pre-seeded `OrderNumber` → `IsDuplicateOrder` true, excluded.
  - `Commit` creates `Customer` (name+postal) + `Order` (Open, TcgPlayer, all
    fields incl. the two imported fields); re-`Commit` of the same preview
    creates nothing (idempotent).
- **`Order`** new-field persistence round-trip (schema).
- **`UnifiedMigrationService`** guard extended for the two new columns.
- **`OrdersViewModel`** reconciliation-hint math (added vs target; hidden when
  not imported) — VM test.
- **Human E2E:** real Shipping Export → preview (statuses correct) → import →
  open an imported order → add cards → hint updates → Ship (inventory removed +
  Sell movements) → re-import same file imports nothing.

## 8. Build Phasing (one spec, two green checkpoints)

1. **Model + import service** — `Order.ImportedItemCount/ImportedProductValue` +
   schema/migration; `TcgOrderImportPreview`/`TcgOrderImportRow`;
   `ITcgPlayerOrderImportService` + `TcgPlayerOrderImportService`
   (`PreviewImport` + `Commit`) with tests; DI registration.
2. **UI** — preview dialog (`TcgOrderImportView`/VM + `IDialogService` method);
   Orders-view Import button + command; reconciliation hint in the editor.

## 9. Project Placement

| Artifact | Project |
|----------|---------|
| `Order` new fields | `OmniCard.Shared/Models` |
| `TcgOrderImportPreview`, `TcgOrderImportRow` | `OmniCard.Shared/Models` |
| `ITcgPlayerOrderImportService` | `OmniCard.Shared/Interfaces` |
| `TcgPlayerOrderImportService` | `OmniCard.Collection` |
| schema/migration | `OmniCard.Data` (`OmniCardDbContext`, `UnifiedMigrationService`) |
| `TcgOrderImportView(Model)`, Orders-view wiring, `IDialogService` method | `OmniCard` |
