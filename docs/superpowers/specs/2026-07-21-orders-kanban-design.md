# Orders Kanban & Lifecycle Rework — Design

**Date:** 2026-07-21
**Status:** Approved (pending spec review)
**Author:** Andrew Riebe (with Claude)
**Related:** Sales & Fulfillment phases 2 (orders/ship) and 4 (TCGPlayer import)

## 1. Problem & Goals

After testing the Orders workflow, three gaps surfaced:

1. **No way to delete an order** — test orders accumulate with no cleanup.
2. **The status flow is button-driven and one-way** — you can't move an order
   back from Packed, and the initial state is labelled "Open" (the user wants
   "Created").
3. **Editing a Packed order is still possible**, which shouldn't be allowed.

Goal: replace the status buttons with a **drag-and-drop kanban board**, make
**Created** the only editable state, allow **un-packing** (Packed → Created),
and add **order deletion** for pre-ship orders.

### Success criteria

- Orders appear as cards in a 4-column board (Created · Packed · Shipped ·
  Completed); dragging a card between columns changes its status when the move
  is allowed and is rejected (with a message, no change) when it isn't.
- A card in Created is fully editable; a card in Packed or later is read-only
  until dragged back to Created.
- Cancel and Delete are available from a card's right-click menu; Delete works
  only for pre-ship orders.
- Existing shipped-order accounting (inventory removal + `Sell` movements) is
  unchanged and remains irreversible.

## 2. Decisions (resolved in brainstorming)

- **Rename `Open` → `Created`** in the enum (single source of truth), with a
  data migration for existing rows.
- **Columns / moves:** Created ↔ Packed (both directions), Packed → Shipped
  (one-way), Shipped → Completed (one-way). Cancel is a separate action, allowed
  only pre-ship. No move out of Shipped/Completed (phase 2 has no restock/un-ship).
- **Edit lock:** Created is the only editable state; Packed+ is read-only. To
  edit, drag back to Created.
- **Delete:** allowed only for Created/Packed/Cancelled orders; Shipped/Completed
  cannot be deleted.
- **Drag-drop:** native WPF (no new dependency); the drop→transition logic lives
  in the view-model.
- **Layout:** board replaces the order list; clicking a card selects it and shows
  the existing editor panel; Cancel/Delete are right-click context-menu items.
- **Cancelled orders:** shown in a muted, collapsible "Cancelled" strip below the
  board (kept reachable for view/delete; not a drag target).

## 3. Non-Goals

- Un-shipping / restock (dragging out of Shipped) — unchanged from phase 2.
- Deleting Shipped/Completed orders (protects sales history / P&L).
- Reworking the receipt, import, or P&L features (only their host view moves).
- A third-party drag-drop library.

## 4. Status Model & Migration

`OmniCard.Shared/Models/OrderStatus.cs`:
```
enum OrderStatus { Created, Packed, Shipped, Completed, Cancelled }
```
(`Created` replaces `Open`; ordering keeps `Created` first so the default enum
value stays the initial state.)

**Transitions** — a single `IsValidTransition(from, to)` (in `OrdersViewModel`,
reused by both drag and cancel):

| From | Allowed to |
|------|-----------|
| Created | Packed, Cancelled |
| Packed | Created, Shipped, Cancelled |
| Shipped | Completed |
| Completed | — |
| Cancelled | — |

The **Packed → Shipped** transition still runs the existing `OrderService.SetStatus`
ship logic (inventory decrement + `Sell` movements + mark listings sold); no other
transition touches inventory.

**Migration** (`OmniCard.Data/UnifiedMigrationService.cs`):
- Fresh DBs: `CREATE TABLE ... Status TEXT NOT NULL DEFAULT 'Created'`.
- Existing DBs: one-time idempotent `UPDATE Orders SET Status='Created' WHERE Status='Open'`
  in `EnsureUnifiedSchema` (safe to run every startup — after the rename no `'Open'`
  rows remain). Listings/other tables are unaffected (only `Orders.Status` used `'Open'`).

**Reference updates:** `OrderService.CreateOrder` (default `Created`), the phase-4
`TcgPlayerOrderImportService` (`Status = OrderStatus.Created`), `OrdersView` XAML
`x:Static` bindings, and all tests referencing `OrderStatus.Open`.

## 5. Delete

`IOrderService.DeleteOrder(int orderId)`:
- No-op if the order doesn't exist.
- Throws `InvalidOperationException` if status is `Shipped` or `Completed`.
- Otherwise deletes the `Order` and its `OrderLine`s in one context.

No inventory/movement cleanup is required: pre-ship orders never decremented lots
or recorded `Sell` movements, and `GetActiveListings` already excludes lots that
sit on an active order line — so once the lines are gone, those lots are sellable
again automatically.

## 6. UI

### Board (`OrdersView`, Sales ▸ Orders sub-tab)
- Top bar: **New Order** (customer combo + button) and **Import from TCGPlayer
  CSV…** (both existing).
- **4 columns** — Created · Packed · Shipped · Completed — each a scrollable list
  of order **cards** showing order # (or "(no number)"), customer name, item
  count, and total. Cards are drag sources; columns are drop targets.
- **Cancelled strip:** a muted, collapsible list below the board for
  `Cancelled` orders (view/right-click only; not a drop target).
- **Native WPF drag-drop:** card `PreviewMouseMove` → `DragDrop.DoDragDrop`
  carrying the `Order`; column `Drop` → code-behind calls
  `OrdersViewModel.MoveOrder(order, targetStatus)`. Invalid moves surface
  `StatusMessage` and leave the card put.
- **Right-click menu** on a card: **Cancel order** (pre-ship only) and
  **Delete order…** (pre-ship only; confirm dialog).

### Editor panel (right side, existing content)
- Customer/order fields, add-card picker, lines grid, Save, Print Receipt,
  Export PDF, reconciliation hint — as today.
- **Enabled only when the selected order is `Created`** (`IsEditable`); Packed+
  renders the editor read-only. The old Pack/Ship/Complete/Cancel buttons are
  removed (status changes via drag; Cancel via right-click). **Save** stays.

## 7. View-Model (`OrdersViewModel`)

- Per-status collections populated in `Load()`: `CreatedOrders`, `PackedOrders`,
  `ShippedOrders`, `CompletedOrders`, `CancelledOrders`.
- `bool IsEditable => SelectedOrder?.Status == OrderStatus.Created;` (raise on
  `SelectedOrder` change). Editor `IsEnabled` binds to it; `AddCard`/`RemoveLine`
  guards change from `!= Open` to `!= Created`.
- `void MoveOrder(Order order, OrderStatus target)` — validates via
  `IsValidTransition`; on success calls `orderService.SetStatus`, reloads, keeps
  selection; on failure sets `StatusMessage`.
- `[RelayCommand] CancelOrder(Order)` — `MoveOrder(order, Cancelled)` (guarded).
- `[RelayCommand] DeleteOrder(Order)` — confirm, `orderService.DeleteOrder`,
  reload; guarded to pre-ship with a friendly message otherwise.
- `IsValidTransition` updated per §4 (notably adds `Packed → Created`).

## 8. Testing Strategy

- **Migration:** existing `Orders` rows with `Status='Open'` become `'Created'`
  after `EnsureUnifiedSchema`; fresh DB default is `'Created'`.
- **`OrderService.DeleteOrder`:** deletes a Created/Packed/Cancelled order + its
  lines; throws for Shipped/Completed; a deleted pre-ship order's lot reappears in
  `GetActiveListings`.
- **`OrdersViewModel`:** `IsValidTransition` table (esp. Packed→Created allowed,
  Shipped→Created and Completed→anything rejected); `MoveOrder` applies/refuses;
  `CancelOrder`/`DeleteOrder` guards; `IsEditable` true only for Created; `Load`
  buckets orders into the right per-status collections.
- **Ship path unchanged:** the existing ship test (inventory removal + `Sell`)
  still passes after the rename.
- **Human E2E:** drag cards across columns (valid + rejected moves); un-pack;
  editor locks outside Created; right-click Cancel/Delete; delete frees the lot;
  Cancelled strip.

## 9. Build Phasing (two green checkpoints)

1. **Domain** — rename `Open→Created` (enum + migration + all refs); transition
   rules (+ Packed→Created); `IOrderService.DeleteOrder` + impl. Service/VM tests.
2. **Kanban UI** — `OrdersView` board + Cancelled strip + native drag-drop +
   right-click Cancel/Delete; `OrdersViewModel` per-status collections + `MoveOrder`
   + `IsEditable` + editor locking. VM tests + human E2E.

## 10. Project Placement & Branch

| Artifact | Project |
|----------|---------|
| `OrderStatus` rename | `OmniCard.Shared/Models` |
| migration (rename data fix) | `OmniCard.Data` |
| `IOrderService.DeleteOrder` | `OmniCard.Shared/Interfaces` + `OmniCard.Collection` |
| board/editor/drag-drop/VM | `OmniCard/Views/Sales` |

**Branch:** based on `feat/sales-fulfillment-phase4` (shares the Orders view and
the import's initial-status reference), so the full Orders experience is testable
together; merges after phase 4.
