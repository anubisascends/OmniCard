# Orders Kanban & Lifecycle Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Orders status buttons with a drag-and-drop kanban board, rename the first status Open→Created, make Created the only editable state, allow un-packing (Packed→Created), and add pre-ship order deletion.

**Architecture:** `OrderStatus.Open` becomes `Created` (enum + idempotent data migration). A new `IsValidTransition` allows Created↔Packed, Packed→Shipped, Shipped→Completed, and Cancel (pre-ship). `OrderService.DeleteOrder` removes pre-ship orders + lines (freeing their lots). `OrdersViewModel` gains per-status collections + `MoveOrder`/`CancelOrder`/`DeleteOrder` + an `IsEditable` flag. `OrdersView` becomes a 4-column board (+ a Cancelled strip) with native WPF drag-drop and a right-click menu; the editor panel is enabled only for Created orders. The Ship transition's inventory/`Sell` accounting is unchanged and stays irreversible.

**Tech Stack:** C# / .NET, WPF (CommunityToolkit.Mvvm; native `System.Windows.DragDrop`), EF Core (SQLite), xUnit, Moq.

**Spec:** `docs/superpowers/specs/2026-07-21-orders-kanban-design.md`

## Global Constraints

- **Rename `Open` → `Created`** as an enum value; `Created` stays the FIRST enum member (default value = initial state). Update EVERY reference (production + tests + XAML + schema).
- **Migration is idempotent:** `UPDATE Orders SET Status='Created' WHERE Status='Open'` is safe to run on every startup.
- **Ship stays irreversible** — no un-ship/restock; only `Packed→Shipped` touches inventory (unchanged `OrderService.SetStatus`).
- **Delete only pre-ship** (Created/Packed/Cancelled); Shipped/Completed throw.
- **Native WPF drag-drop only** — no new NuGet dependency.
- **No `.sln`** — build AND test via `dotnet build OmniCard.Tests/OmniCard.Tests.csproj` and `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`.
- **MVVM:** CommunityToolkit `[ObservableProperty]`/`[RelayCommand]`; drag mechanics live in `OrdersView.xaml.cs` code-behind and call VM methods.
- **Branch:** `feat/orders-kanban` off `feat/sales-fulfillment-phase4` (shares the Orders view + import's initial-status ref). **`docs/` gitignored** — commit only code/tests.

---

## File Structure

**Modified:**
- `OmniCard.Shared/Models/OrderStatus.cs` — enum rename.
- `OmniCard.Data/UnifiedMigrationService.cs` — CREATE-TABLE default + rename UPDATE.
- `OmniCard.Collection/OrderService.cs` — `CreateOrder` default + new `DeleteOrder`.
- `OmniCard.Collection/ListingService.cs` — committed-lot exclusion uses Created.
- `OmniCard.Collection/TcgPlayerOrderImportService.cs` — import sets Created.
- `OmniCard.Shared/Interfaces/IOrderService.cs` — add `DeleteOrder`.
- `OmniCard/Views/Sales/OrdersViewModel.cs` — collections, MoveOrder, Cancel/Delete, IsEditable, transitions, guards; remove `SetStatus`.
- `OmniCard/Views/Sales/OrdersView.xaml` (+ `.xaml.cs`) — kanban board, strip, drag-drop, context menu, editor lock.
- Tests: `UnifiedMigrationTests`, `OrderServiceTests`, `OrdersViewModelTests`, plus mechanical `OrderStatus.Open`→`Created` in `AnalyticsServiceTests`, `ListingServiceTests`, `TcgPlayerOrderImportServiceTests`.

---

## BUILD STEP 1 — Domain (rename + delete)

### Task 1: Rename OrderStatus.Open → Created (+ migration)

**Files:**
- Modify: `OmniCard.Shared/Models/OrderStatus.cs`, `OmniCard.Data/UnifiedMigrationService.cs`, `OmniCard.Collection/OrderService.cs`, `OmniCard.Collection/ListingService.cs`, `OmniCard.Collection/TcgPlayerOrderImportService.cs`, `OmniCard/Views/Sales/OrdersViewModel.cs`
- Test: `OmniCard.Tests/Services/UnifiedMigrationTests.cs` + mechanical updates to `AnalyticsServiceTests.cs`, `ListingServiceTests.cs`, `OrderServiceTests.cs`, `TcgPlayerOrderImportServiceTests.cs`, `OrdersViewModelTests.cs`

**Interfaces:**
- Produces: `OrderStatus { Created, Packed, Shipped, Completed, Cancelled }`. Behavior otherwise identical this task (transition semantics change in Task 3).

- [ ] **Step 1: Write the failing migration test**

Add to `OmniCard.Tests/Services/UnifiedMigrationTests.cs` (inside the class):

```csharp
    [Fact]
    public void EnsureUnifiedSchema_RenamesOpenOrderStatus_ToCreated()
    {
        var dir = Path.Combine(_tempDir, "open-to-created");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "inventory.db");
        using (var seed = new SqliteConnection($"Data Source={dbPath}"))
        {
            seed.Open();
            using var cmd = seed.CreateCommand();
            // Pre-rename Orders table with an 'Open' row.
            cmd.CommandText = """
                CREATE TABLE Orders (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT, CustomerId INTEGER NOT NULL,
                    Channel TEXT NOT NULL DEFAULT 'Manual', OrderNumber TEXT,
                    OrderDate TEXT NOT NULL, Status TEXT NOT NULL DEFAULT 'Open',
                    TrackingNumber TEXT, Carrier TEXT,
                    ShippingChargedToBuyer TEXT NOT NULL DEFAULT '0',
                    ShippingCost TEXT NOT NULL DEFAULT '0', MarketplaceFees TEXT NOT NULL DEFAULT '0',
                    Notes TEXT, CreatedAt TEXT NOT NULL, ShippedAt TEXT);
                INSERT INTO Orders (CustomerId, OrderDate, Status, ShippingChargedToBuyer, ShippingCost, MarketplaceFees, CreatedAt)
                VALUES (1, '2026-07-17', 'Open', '0', '0', '0', '2026-07-17');
                """;
            cmd.ExecuteNonQuery();
        }

        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            UnifiedMigrationService.EnsureUnifiedSchema(conn);
            using var verify = conn.CreateCommand();
            verify.CommandText = "SELECT COUNT(*) FROM Orders WHERE Status='Open'";
            Assert.Equal(0L, (long)verify.ExecuteScalar()!);
            verify.CommandText = "SELECT COUNT(*) FROM Orders WHERE Status='Created'";
            Assert.Equal(1L, (long)verify.ExecuteScalar()!);
        }
    }
```

(Match `_tempDir`/`EnsureUnifiedSchema` access to the sibling tests in the file; if `EnsureUnifiedSchema` is invoked differently there, follow that pattern.)

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~EnsureUnifiedSchema_RenamesOpenOrderStatus_ToCreated`
Expected: FAIL (rows still `'Open'`).

- [ ] **Step 3: Rename the enum**

`OmniCard.Shared/Models/OrderStatus.cs`:

```csharp
namespace OmniCard.Models;

public enum OrderStatus { Created, Packed, Shipped, Completed, Cancelled }
```

- [ ] **Step 4: Update the schema + add the rename migration**

In `OmniCard.Data/UnifiedMigrationService.cs`:
- In the `CREATE TABLE IF NOT EXISTS Orders` statement, change `Status TEXT NOT NULL DEFAULT 'Open'` → `Status TEXT NOT NULL DEFAULT 'Created'`.
- Add the one-time rename right after the `if (TableExists(cmd, "Orders")) { ... }` guard block (the one adding the phase-4 columns):

```csharp
        if (TableExists(cmd, "Orders"))
        {
            cmd.CommandText = "UPDATE Orders SET Status = 'Created' WHERE Status = 'Open'";
            cmd.ExecuteNonQuery();
        }
```

(If the phase-4 `if (TableExists(cmd, "Orders"))` block already exists, add the UPDATE inside it after the `AddColumnIfMissing` calls rather than a second guard.)

- [ ] **Step 5: Update production references**

- `OmniCard.Collection/OrderService.cs:38` — `Status = OrderStatus.Created,`
- `OmniCard.Collection/TcgPlayerOrderImportService.cs:98` — `Status = OrderStatus.Created,`
- `OmniCard.Collection/ListingService.cs:186` — `&& (order.Status == OrderStatus.Created || order.Status == OrderStatus.Packed)`
- `OmniCard/Views/Sales/OrdersViewModel.cs` — mechanical substitution only (semantics unchanged this task):
  - lines 96, 106 guards: `!= OrderStatus.Created` and message `"Can only edit a Created order."`
  - `IsValidTransition` (lines 222-226): replace each `OrderStatus.Open` with `OrderStatus.Created` and the `OrderStatus.Open => false` arm with `OrderStatus.Created => false`. Keep the same shape:
    ```csharp
    private static bool IsValidTransition(OrderStatus from, OrderStatus to) => to switch
    {
        OrderStatus.Packed => from is OrderStatus.Created,
        OrderStatus.Shipped => from is OrderStatus.Created or OrderStatus.Packed,
        OrderStatus.Completed => from is OrderStatus.Shipped,
        OrderStatus.Cancelled => from is OrderStatus.Created or OrderStatus.Packed,
        OrderStatus.Created => false,
        _ => false,
    };
    ```

- [ ] **Step 6: Update test references (mechanical)**

Replace `OrderStatus.Open` with `OrderStatus.Created` at each site:
- `AnalyticsServiceTests.cs:336`
- `ListingServiceTests.cs:457`
- `OrderServiceTests.cs:129,151,170`
- `TcgPlayerOrderImportServiceTests.cs:90,112`
- `OrdersViewModelTests.cs:13` (the `NewOrder` default param), `96,136,171,192,206`
- `UnifiedMigrationTests.cs:825` — this seeds a pre-existing Orders table with `DEFAULT 'Open'` to simulate an OLD db; **leave it as `'Open'`** (it's simulating pre-rename data, which the new migration converts). Do not change this one.

- [ ] **Step 7: Run to verify pass + full suite**

Run: `dotnet build OmniCard.Tests/OmniCard.Tests.csproj` then `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: Build 0 errors; all tests pass (incl. the new migration test).

- [ ] **Step 8: Commit**

```bash
git add OmniCard.Shared/Models/OrderStatus.cs OmniCard.Data/UnifiedMigrationService.cs \
        OmniCard.Collection/OrderService.cs OmniCard.Collection/ListingService.cs \
        OmniCard.Collection/TcgPlayerOrderImportService.cs OmniCard/Views/Sales/OrdersViewModel.cs \
        OmniCard.Tests/
git commit -m "refactor(sales): rename OrderStatus.Open -> Created (+ data migration)"
```

---

### Task 2: OrderService.DeleteOrder (pre-ship only)

**Files:**
- Modify: `OmniCard.Shared/Interfaces/IOrderService.cs`, `OmniCard.Collection/OrderService.cs`
- Test: `OmniCard.Tests/Services/OrderServiceTests.cs`

**Interfaces:**
- Produces: `IOrderService.DeleteOrder(int orderId)` — deletes order + lines; throws `InvalidOperationException` for Shipped/Completed; no-op if missing.

- [ ] **Step 1: Write the failing tests**

Add to `OmniCard.Tests/Services/OrderServiceTests.cs`:

```csharp
    [Fact]
    public void DeleteOrder_RemovesOrderAndLines_WhenPreShip()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "DEL-1");
        svc.AddLine(order.Id, lotId, 3.50m);

        svc.DeleteOrder(order.Id);

        Assert.Null(svc.GetOrder(order.Id));
        Assert.Empty(svc.GetLines(order.Id));
    }

    [Fact]
    public void DeleteOrder_Throws_WhenShippedOrCompleted()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "DEL-2");
        svc.AddLine(order.Id, lotId, 3.50m);
        svc.SetStatus(order.Id, OrderStatus.Shipped);

        Assert.Throws<InvalidOperationException>(() => svc.DeleteOrder(order.Id));
        Assert.NotNull(svc.GetOrder(order.Id));
    }

    [Fact]
    public void DeleteOrder_NoOp_WhenMissing()
    {
        var svc = OrderSvc();
        svc.DeleteOrder(999999); // must not throw
    }
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~OrderServiceTests`
Expected: FAIL — `DeleteOrder` doesn't exist.

- [ ] **Step 3: Add to the interface**

In `OmniCard.Shared/Interfaces/IOrderService.cs`, add:

```csharp
    /// <summary>Deletes a pre-ship order and its lines. Throws if the order is Shipped or
    /// Completed (its sale is recorded and inventory already removed).</summary>
    void DeleteOrder(int orderId);
```

- [ ] **Step 4: Implement in `OrderService`**

Add to `OmniCard.Collection/OrderService.cs`:

```csharp
    public void DeleteOrder(int orderId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var order = ctx.Orders.FirstOrDefault(o => o.Id == orderId);
        if (order is null) return;
        if (order.Status is OrderStatus.Shipped or OrderStatus.Completed)
            throw new InvalidOperationException(
                $"Can't delete a {order.Status} order (its sale is recorded and inventory removed).");

        var lines = ctx.OrderLines.Where(l => l.OrderId == orderId).ToList();
        ctx.OrderLines.RemoveRange(lines);
        ctx.Orders.Remove(order);
        ctx.SaveChanges();
    }
```

- [ ] **Step 5: Run to verify pass + full suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Interfaces/IOrderService.cs OmniCard.Collection/OrderService.cs \
        OmniCard.Tests/Services/OrderServiceTests.cs
git commit -m "feat(sales): OrderService.DeleteOrder for pre-ship orders"
```

---

## BUILD STEP 2 — Kanban UI

### Task 3: OrdersViewModel kanban restructure

**Files:**
- Modify: `OmniCard/Views/Sales/OrdersViewModel.cs`, `OmniCard.Tests/Views/Sales/OrdersViewModelTests.cs`

**Interfaces:**
- Consumes: `IOrderService.DeleteOrder` (Task 2).
- Produces: per-status `ObservableCollection<Order>` (`CreatedOrders`/`PackedOrders`/`ShippedOrders`/`CompletedOrders`/`CancelledOrders`); `bool IsEditable`; `void MoveOrder(Order, OrderStatus)`; `[RelayCommand] CancelOrder(Order)`; `[RelayCommand] DeleteOrder(Order)`. Removes `SetStatusCommand`.

- [ ] **Step 1: Write the failing VM tests**

Add to `OmniCard.Tests/Views/Sales/OrdersViewModelTests.cs` (use the file's existing VM-construction helper — it already provides the mocked services incl. `IOrderService`; if `MoveOrder`/`DeleteOrder` need `IOrderService` behavior, set up the mock as neighboring tests do):

```csharp
    [Fact]
    public void MoveOrder_AllowsPackedBackToCreated_AndRejectsOutOfShipped()
    {
        var vm = /* construct as neighboring tests do */;
        Assert.True(OrdersViewModel.IsValidTransitionPublic(OrderStatus.Packed, OrderStatus.Created));
        Assert.True(OrdersViewModel.IsValidTransitionPublic(OrderStatus.Created, OrderStatus.Packed));
        Assert.True(OrdersViewModel.IsValidTransitionPublic(OrderStatus.Packed, OrderStatus.Shipped));
        Assert.False(OrdersViewModel.IsValidTransitionPublic(OrderStatus.Shipped, OrderStatus.Created));
        Assert.False(OrdersViewModel.IsValidTransitionPublic(OrderStatus.Completed, OrderStatus.Shipped));
        Assert.True(OrdersViewModel.IsValidTransitionPublic(OrderStatus.Created, OrderStatus.Cancelled));
        Assert.False(OrdersViewModel.IsValidTransitionPublic(OrderStatus.Shipped, OrderStatus.Cancelled));
    }

    [Fact]
    public void IsEditable_TrueOnlyForCreated()
    {
        var vm = /* construct as neighboring tests do; seed one Created + one Packed order via the mocked IOrderService.GetOrders */;
        vm.Load();
        vm.SelectedOrder = vm.CreatedOrders.First();
        Assert.True(vm.IsEditable);
        vm.SelectedOrder = vm.PackedOrders.First();
        Assert.False(vm.IsEditable);
    }

    [Fact]
    public void Load_BucketsOrdersByStatus()
    {
        var vm = /* construct with mocked GetOrders returning one order per status */;
        vm.Load();
        Assert.Single(vm.CreatedOrders);
        Assert.Single(vm.PackedOrders);
        Assert.Single(vm.ShippedOrders);
        Assert.Single(vm.CompletedOrders);
        Assert.Single(vm.CancelledOrders);
    }
```

*Note:* the existing `OrdersViewModelTests` mock `IOrderService` (Moq). For `Load` bucketing, set `mock.Setup(s => s.GetOrders()).Returns(...)` with one order per status. Expose the transition check for testing by making the method `internal static bool IsValidTransitionPublic(...)` OR keep `IsValidTransition` private and test transitions via `MoveOrder` on a mocked service (assert `SetStatus` called / not called). Prefer the latter if you don't want a test-only accessor — adapt the first test to drive `MoveOrder` and verify `orderService.SetStatus`/`StatusMessage`. Pick one approach and make the test assert real behavior.

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~OrdersViewModelTests`
Expected: FAIL — new members don't exist.

- [ ] **Step 3: Restructure `OrdersViewModel`**

In `OmniCard/Views/Sales/OrdersViewModel.cs`:

Add the per-status collections (next to `Orders`):

```csharp
    public ObservableCollection<Order> CreatedOrders { get; } = [];
    public ObservableCollection<Order> PackedOrders { get; } = [];
    public ObservableCollection<Order> ShippedOrders { get; } = [];
    public ObservableCollection<Order> CompletedOrders { get; } = [];
    public ObservableCollection<Order> CancelledOrders { get; } = [];
```

Add `IsEditable` and notify it on selection change:

```csharp
    public bool IsEditable => SelectedOrder?.Status == OrderStatus.Created;
```

In `OnSelectedOrderChanged`, add `OnPropertyChanged(nameof(IsEditable));` alongside the existing notifications.

Rewrite `Load` to also bucket (keep the flat `Orders` too — harmless, still used by tests/selection):

```csharp
    public void Load()
    {
        Orders.Clear();
        CreatedOrders.Clear(); PackedOrders.Clear(); ShippedOrders.Clear();
        CompletedOrders.Clear(); CancelledOrders.Clear();
        foreach (var o in orderService.GetOrders())
        {
            Orders.Add(o);
            (o.Status switch
            {
                OrderStatus.Created => CreatedOrders,
                OrderStatus.Packed => PackedOrders,
                OrderStatus.Shipped => ShippedOrders,
                OrderStatus.Completed => CompletedOrders,
                _ => CancelledOrders,
            }).Add(o);
        }
        Customers.Clear();
        foreach (var c in customerService.GetAll()) Customers.Add(c);
        AvailableCards.Clear();
        foreach (var a in listingService.GetActiveListings()) AvailableCards.Add(a);
    }
```

Replace the `SetStatus` `[RelayCommand]` with `MoveOrder` + `CancelOrder` + `DeleteOrder`:

```csharp
    /// <summary>Applies a drag-drop status move (or programmatic move). Validates the transition;
    /// on Packed→Shipped this runs the existing ship accounting via OrderService.SetStatus.</summary>
    public void MoveOrder(Order? order, OrderStatus target)
    {
        if (order is null) return;
        if (order.Status == target) return;
        if (!IsValidTransition(order.Status, target))
        {
            StatusMessage = $"Can't move {order.Status} → {target}.";
            return;
        }
        orderService.SetStatus(order.Id, target);
        var id = order.Id;
        Load();
        SelectedOrder = Orders.FirstOrDefault(o => o.Id == id);
        StatusMessage = $"Order moved to {target}.";
    }

    [RelayCommand]
    public void CancelOrder(Order? order)
    {
        if (order is null) return;
        if (!IsValidTransition(order.Status, OrderStatus.Cancelled))
        {
            StatusMessage = $"Can't cancel a {order.Status} order.";
            return;
        }
        MoveOrder(order, OrderStatus.Cancelled);
    }

    [RelayCommand]
    public void DeleteOrder(Order? order)
    {
        if (order is null) return;
        if (order.Status is OrderStatus.Shipped or OrderStatus.Completed)
        {
            StatusMessage = $"Can't delete a {order.Status} order.";
            return;
        }
        try
        {
            orderService.DeleteOrder(order.Id);
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            return;
        }
        var wasSelected = SelectedOrder?.Id == order.Id;
        Load();
        if (wasSelected) SelectedOrder = null;
        StatusMessage = "Order deleted.";
    }
```

Update `IsValidTransition` to the new rules (the un-pack is `Packed → Created`):

```csharp
    private static bool IsValidTransition(OrderStatus from, OrderStatus to) => to switch
    {
        OrderStatus.Created => from is OrderStatus.Packed,          // un-pack
        OrderStatus.Packed => from is OrderStatus.Created,
        OrderStatus.Shipped => from is OrderStatus.Packed,
        OrderStatus.Completed => from is OrderStatus.Shipped,
        OrderStatus.Cancelled => from is OrderStatus.Created or OrderStatus.Packed,
        _ => false,
    };
```

Change the `AddCard`/`RemoveLine` guards to reference `IsEditable` (clearer) and update the message:

```csharp
        if (!IsEditable) { StatusMessage = "Only a Created order can be edited."; return; }
```

*(If you exposed a test accessor in Step 1, add `internal static bool IsValidTransitionPublic(OrderStatus f, OrderStatus t) => IsValidTransition(f, t);`.)*

Remove the now-unused `SetStatus` `[RelayCommand]` entirely (the board uses `MoveOrder`).

- [ ] **Step 4: Run VM tests + full suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: PASS. Update any existing `OrdersViewModelTests` that referenced `SetStatusCommand` to use `MoveOrder`/`CancelOrder` instead (the phase-2 transition test asserted `SetStatusCommand` behavior — port it to `MoveOrder`, keeping the intent: invalid moves don't call `SetStatus`).

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Sales/OrdersViewModel.cs OmniCard.Tests/Views/Sales/OrdersViewModelTests.cs
git commit -m "feat(sales): OrdersViewModel kanban model — per-status columns, MoveOrder, Cancel/Delete, IsEditable"
```

---

### Task 4: OrdersView kanban board + drag-drop + editor lock

**Files:**
- Modify: `OmniCard/Views/Sales/OrdersView.xaml`, `OmniCard/Views/Sales/OrdersView.xaml.cs`

**Interfaces:**
- Consumes: `OrdersViewModel` per-status collections, `MoveOrder`, `CancelOrderCommand`, `DeleteOrderCommand`, `IsEditable` (Task 3).

*Verified by build + human E2E (WPF drag-drop). No new unit tests (the move logic is tested in Task 3).*

- [ ] **Step 1: Rebuild `OrdersView.xaml`**

Replace the `<Grid>` body of `OmniCard/Views/Sales/OrdersView.xaml` with a board + editor. Keep the root `UserControl` element (with its existing `xmlns` incl. `conv`, `models`, `Loaded="OrdersView_OnLoaded"`). Board area:

```xml
    <Grid Margin="8">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="2*"/>
            <ColumnDefinition Width="360"/>
        </Grid.ColumnDefinitions>

        <!-- Board -->
        <DockPanel Grid.Column="0" Margin="0,0,8,0">
            <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
                <ComboBox Width="160" Margin="0,0,8,0"
                          ItemsSource="{Binding Customers}" SelectedItem="{Binding SelectedCustomer}"
                          DisplayMemberPath="Name"/>
                <Button Content="New Order" Command="{Binding NewOrderCommand}" Padding="12,4" Margin="0,0,8,0"/>
                <Button Content="Import from TCGPlayer CSV…" Command="{Binding ImportTcgPlayerCommand}" Padding="12,4"/>
            </StackPanel>

            <!-- Cancelled strip (collapsible, not a drop target) -->
            <Expander DockPanel.Dock="Bottom" Header="Cancelled" Margin="0,8,0,0"
                      IsExpanded="False">
                <ItemsControl ItemsSource="{Binding CancelledOrders}" Margin="4">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                                    Opacity="0.6" CornerRadius="4" Padding="8,4" Margin="0,0,0,4"
                                    MouseLeftButtonUp="Card_Select" Tag="{Binding}">
                                <Border.ContextMenu>
                                    <ContextMenu>
                                        <MenuItem Header="Delete order…"
                                                  Command="{Binding PlacementTarget.Tag.DataContext.DeleteOrderCommand,
                                                             RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                                                  CommandParameter="{Binding}"/>
                                    </ContextMenu>
                                </Border.ContextMenu>
                                <TextBlock Text="{Binding OrderNumber, TargetNullValue='(no number)'}"/>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </Expander>

            <!-- 4 columns -->
            <UniformGrid Rows="1">
                <local:OrderColumn Header="Created"   ItemsSource="{Binding CreatedOrders}"/>
                <local:OrderColumn Header="Packed"    ItemsSource="{Binding PackedOrders}"/>
                <local:OrderColumn Header="Shipped"   ItemsSource="{Binding ShippedOrders}"/>
                <local:OrderColumn Header="Completed" ItemsSource="{Binding CompletedOrders}"/>
            </UniformGrid>
        </DockPanel>

        <!-- Editor -->
        <DockPanel Grid.Column="1"
                   Visibility="{Binding SelectedOrder, Converter={conv:NullToVisibilityConverter}}">
            <!-- ... existing editor content (header fields, add-card, lines, Save/Receipt/Export,
                 total, reconciliation hint, status message) — see Step 3 for the IsEnabled change ... -->
        </DockPanel>
    </Grid>
```

*Simplify:* rather than a custom `OrderColumn` control, inline each column as a `DockPanel` with a header `TextBlock` + a `ListBox` (drop target). Use this repeated block per column (set `Header` text and `ItemsSource`, and `Tag` = the target status string so the drop handler knows the destination):

```xml
                <DockPanel Margin="2">
                    <TextBlock DockPanel.Dock="Top" Text="Created" FontWeight="Bold" Margin="4"/>
                    <ListBox ItemsSource="{Binding CreatedOrders}" AllowDrop="True"
                             Tag="Created" Drop="Column_Drop" DragOver="Column_DragOver"
                             SelectedItem="{Binding SelectedOrder}"
                             ScrollViewer.VerticalScrollBarVisibility="Auto">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <Border Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                                        CornerRadius="4" Padding="8" Margin="0,0,0,6"
                                        PreviewMouseLeftButtonDown="Card_MouseDown"
                                        PreviewMouseMove="Card_MouseMove" Tag="{Binding}">
                                    <Border.ContextMenu>
                                        <ContextMenu>
                                            <MenuItem Header="Cancel order"
                                                      Command="{Binding PlacementTarget.Tag.DataContext.CancelOrderCommand,
                                                                 RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                                                      CommandParameter="{Binding}"/>
                                            <MenuItem Header="Delete order…"
                                                      Command="{Binding PlacementTarget.Tag.DataContext.DeleteOrderCommand,
                                                                 RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                                                      CommandParameter="{Binding}"/>
                                        </ContextMenu>
                                    </Border.ContextMenu>
                                    <StackPanel>
                                        <TextBlock Text="{Binding OrderNumber, TargetNullValue='(no number)'}" FontWeight="SemiBold"/>
                                    </StackPanel>
                                </Border>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                </DockPanel>
```

Repeat for Packed/Shipped/Completed (change the header `Text`, the `ItemsSource`, and `Tag`). *The `PlacementTarget.Tag.DataContext` binding path assumes the card `Border.Tag` is the order and its DataContext chain reaches the VM — simpler and more robust: bind the context-menu command to the root UserControl's DataContext via a named element. If the `PlacementTarget` path proves finicky at runtime, set each `MenuItem.Command` using `Command="{Binding DataContext.CancelOrderCommand, Source={x:Reference OrdersRoot}}"` where the `UserControl` has `x:Name="OrdersRoot"`, and `CommandParameter="{Binding}"`.*

To keep the plan concrete, name the root `UserControl` `x:Name="OrdersRoot"` and use the `x:Reference` form for all four columns' context-menu commands.

- [ ] **Step 2: Editor `IsEnabled` = `IsEditable`**

Wrap the editor's editable controls so they disable outside Created. Simplest: put `IsEnabled="{Binding IsEditable}"` on the header-fields `Grid`, the add-card `DockPanel`, and the lines `DataGrid` (so Remove buttons disable too). Leave **Save**, **Print Receipt**, **Export PDF** outside that (Receipt/Export should work in any status; Save is harmless when nothing's editable). Keep the total + reconciliation hint + status message visible always.

- [ ] **Step 3: Drag-drop + selection code-behind**

Replace `OmniCard/Views/Sales/OrdersView.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

public partial class OrdersView : UserControl
{
    private Point _dragStart;
    private Order? _dragOrder;

    public OrdersView() => InitializeComponent();

    private void OrdersView_OnLoaded(object sender, RoutedEventArgs e) =>
        (DataContext as OrdersViewModel)?.Load();

    private void Card_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragOrder = (sender as FrameworkElement)?.Tag as Order;
    }

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragOrder is null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        DragDrop.DoDragDrop((DependencyObject)sender, _dragOrder, DragDropEffects.Move);
    }

    private void Column_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(Order)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Column_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Order)) is not Order order) return;
        if ((sender as FrameworkElement)?.Tag is not string statusText) return;
        if (!Enum.TryParse<OrderStatus>(statusText, out var target)) return;
        (DataContext as OrdersViewModel)?.MoveOrder(order, target);
        e.Handled = true;
    }

    private void Card_Select(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is OrdersViewModel vm && (sender as FrameworkElement)?.Tag is Order order)
            vm.SelectedOrder = order;
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build OmniCard.Tests/OmniCard.Tests.csproj`
Expected: Build 0 errors. Resolve any XAML binding/namespace issues (esp. the context-menu command bindings — use the `x:Reference OrdersRoot` form).

- [ ] **Step 5: Full suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: PASS (no VM regressions).

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/Sales/OrdersView.xaml OmniCard/Views/Sales/OrdersView.xaml.cs
git commit -m "feat(sales): Orders kanban board with drag-drop, Cancelled strip, right-click cancel/delete, Created-only editing"
```

- [ ] **Step 7: Human E2E note**

Flag for the reviewer: Sales ▸ Orders shows 4 columns. Drag a Created card → Packed (moves); drag it back → Created (un-pack); drag Packed → Shipped (card moves, inventory removed + Sell recorded, editor locks); try dragging Shipped → Created (rejected, status message); editor is editable only for Created cards; right-click → Cancel (Created/Packed only, card moves to the Cancelled strip); right-click → Delete on a pre-ship card (confirm, card gone, its lot reappears in the add-card picker); Delete refused on a Shipped card.

---

## Self-Review

**1. Spec coverage:**
- §4 rename Open→Created + migration + all refs → Task 1. ✅
- §4 new transition table (incl. Packed→Created) → Task 3 (`IsValidTransition`). ✅
- §5 `DeleteOrder` pre-ship only, frees lot → Task 2 (+ freeing is automatic via `GetActiveListings`, which Task 1 updated to exclude Created/Packed). ✅
- §6 board (4 columns + Cancelled strip), native drag-drop, right-click Cancel/Delete, editor enabled only for Created → Task 4. ✅
- §7 per-status collections, `MoveOrder`/`CancelOrder`/`DeleteOrder`, `IsEditable`, remove `SetStatus` → Task 3. ✅
- §8 tests: migration, DeleteOrder (+throw), transitions, IsEditable, bucketing, ship-unchanged → Tasks 1–3. ✅
- §9 two checkpoints → Step 1 (Tasks 1-2), Step 2 (Tasks 3-4). ✅
- Non-goals honored: no un-ship/restock; no delete of shipped; no drag-drop library. ✅

**2. Placeholder scan:** Task 3 Step 1 and Task 4 Step 1 carry "construct as neighboring tests do" / "if the PlacementTarget path proves finicky" notes — these point at concrete existing patterns with a named fallback (`x:Reference OrdersRoot`), not vague TODOs. All code steps carry complete code.

**3. Type consistency:** `OrderStatus.Created` used consistently after Task 1. `MoveOrder(Order?, OrderStatus)`, `CancelOrderCommand`, `DeleteOrderCommand`, `IsEditable`, and the per-status collection names defined in Task 3 match their use in Task 4's XAML/code-behind. `IOrderService.DeleteOrder(int)` matches between Task 2 (definition) and Task 3 (`orderService.DeleteOrder`).

**Known verification points for the implementer (not blockers):**
- `OrdersViewModelTests` that referenced `SetStatusCommand` must be ported to `MoveOrder`/`CancelOrder` (Task 3 Step 4).
- The context-menu command binding: prefer the `x:Reference OrdersRoot` form over `PlacementTarget.Tag.DataContext` if the latter misbehaves (Task 4 Step 1).
- Confirm `EnsureUnifiedSchema` is `public`/`internal`-accessible from the test as the sibling migration tests call it (Task 1 Step 1).
- The Cancelled `Expander`/strip context menu also uses the same command-binding form — keep it consistent with the columns.
