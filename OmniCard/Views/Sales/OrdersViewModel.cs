using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

/// <summary>
/// Backs the Orders tab: order list + editor (customer, add-card, fees/shipping, status/ship)
/// over <see cref="IOrderService"/>, <see cref="ICustomerService"/> and <see cref="IListingService"/>.
/// </summary>
public partial class OrdersViewModel(
    IOrderService orderService,
    ICustomerService customerService,
    IListingService listingService,
    IReceiptService receiptService,
    IReceiptPdfExporter receiptPdfExporter,
    ITcgPlayerOrderImportService importService,
    IDialogService dialogService,
    ISalesSettingsService salesSettings) : ObservableObject
{
    /// <summary>Minimum usable width (px) for the editor panel.</summary>
    public const double MinEditorWidth = 280;
    private double _editorWidth = salesSettings.OrdersEditorWidth is double w and >= MinEditorWidth ? w : 360;

    /// <summary>Editor panel width (px), persisted. Clamped to <see cref="MinEditorWidth"/>.</summary>
    public double EditorWidth
    {
        get => _editorWidth;
        set
        {
            var clamped = value < MinEditorWidth ? MinEditorWidth : value;
            if (clamped == _editorWidth) return;
            _editorWidth = clamped;
            salesSettings.SetOrdersEditorWidth(clamped);
        }
    }

    /// <summary>Whether the editor panel is collapsed (board full-width). Persisted.</summary>
    [ObservableProperty]
    public partial bool IsEditorCollapsed { get; set; } = salesSettings.OrdersEditorCollapsed;

    partial void OnIsEditorCollapsedChanged(bool value) => salesSettings.SetOrdersEditorCollapsed(value);

    [RelayCommand]
    public void CollapseEditor() => IsEditorCollapsed = true;

    [RelayCommand]
    public void ExpandEditor() => IsEditorCollapsed = false;

    public ObservableCollection<Order> Orders { get; } = [];
    public ObservableCollection<Order> CreatedOrders { get; } = [];
    public ObservableCollection<Order> PackedOrders { get; } = [];
    public ObservableCollection<Order> ShippedOrders { get; } = [];
    public ObservableCollection<Order> CompletedOrders { get; } = [];
    public ObservableCollection<Order> CancelledOrders { get; } = [];
    public ObservableCollection<Customer> Customers { get; } = [];
    public ObservableCollection<OrderLine> Lines { get; } = [];
    public ObservableCollection<ActiveListing> AvailableCards { get; } = [];

    [ObservableProperty]
    public partial Order? SelectedOrder { get; set; }

    [ObservableProperty]
    public partial Customer? SelectedCustomer { get; set; }

    [ObservableProperty]
    public partial ActiveListing? SelectedAvailableCard { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>Whether a Completed order is currently unlocked for correction (see
    /// BeginEditCompletedOrder). While true, AddCard/RemoveLine stage changes locally on
    /// <see cref="Lines"/> instead of calling the service immediately — the DB write happens once,
    /// atomically, in SaveEditedOrder.</summary>
    [ObservableProperty]
    public partial bool IsEditingCompletedOrder { get; set; }

    private string? _pendingEditReason;

    public decimal OrderTotal => Lines.Sum(l => l.Quantity * l.UnitSalePrice);

    public bool IsEditable => SelectedOrder?.Status == OrderStatus.Created
        || (SelectedOrder?.Status == OrderStatus.Completed && IsEditingCompletedOrder);

    public bool CanBeginEditCompletedOrder => SelectedOrder?.Status == OrderStatus.Completed && !IsEditingCompletedOrder;

    public bool HasReconciliation =>
        SelectedOrder?.ImportedItemCount is not null || SelectedOrder?.ImportedProductValue is not null;

    public string ReconciliationHint
    {
        get
        {
            if (SelectedOrder is null) return "";
            var addedItems = Lines.Sum(l => l.Quantity);
            var itemPart = SelectedOrder.ImportedItemCount is int ic
                ? $"added {addedItems} of {ic} items"
                : $"added {addedItems} items";
            var valuePart = SelectedOrder.ImportedProductValue is decimal pv
                ? $"{OrderTotal:C} of {pv:C}"
                : $"{OrderTotal:C}";
            return $"{itemPart} · {valuePart}";
        }
    }

    /// <summary>Loads orders, customers and active listings. Safe to call repeatedly (e.g. on
    /// every tab activation).</summary>
    public void Load()
    {
        Orders.Clear();
        CreatedOrders.Clear(); PackedOrders.Clear(); ShippedOrders.Clear();
        CompletedOrders.Clear(); CancelledOrders.Clear();

        Customers.Clear();
        foreach (var c in customerService.GetAll()) Customers.Add(c);

        // Hydrate the display-only card fields (customer name + line count/total) so each
        // kanban card can show them without loading lines per order.
        var customerNames = Customers.ToDictionary(c => c.Id, c => c.Name);
        var summaries = orderService.GetOrderLineSummaries().ToDictionary(s => s.OrderId);

        foreach (var o in orderService.GetOrders())
        {
            o.CustomerNameDisplay = customerNames.GetValueOrDefault(o.CustomerId);
            if (summaries.TryGetValue(o.Id, out var s))
            {
                o.LineItemCount = s.ItemCount;
                o.LineTotal = s.Total;
            }

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

        AvailableCards.Clear();
        foreach (var a in listingService.GetActiveListings()) AvailableCards.Add(a);
    }

    partial void OnSelectedOrderChanged(Order? value)
    {
        // Selecting a card re-opens the editor if it was collapsed (Jira-style).
        if (value is not null && IsEditorCollapsed)
            IsEditorCollapsed = false;

        // Switching orders always exits edit mode without saving — any staged edits are
        // discarded since Lines is about to be reloaded from the DB below anyway.
        _pendingEditReason = null;
        IsEditingCompletedOrder = false;

        Lines.Clear();
        if (value is not null)
            foreach (var l in orderService.GetLines(value.Id)) Lines.Add(l);
        SelectedCustomer = value is null ? null : Customers.FirstOrDefault(c => c.Id == value.CustomerId);
        OnPropertyChanged(nameof(OrderTotal));
        OnPropertyChanged(nameof(HasReconciliation));
        OnPropertyChanged(nameof(ReconciliationHint));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(CanBeginEditCompletedOrder));
        CancelOrderCommand.NotifyCanExecuteChanged();
        DeleteOrderCommand.NotifyCanExecuteChanged();
    }

    // Parameterized (not SelectedOrder-based) so both the detail-panel toolbar (CommandParameter=
    // SelectedOrder) and each kanban card's context menu (CommandParameter=that card's own Order)
    // enable/disable independently and correctly.
    public static bool CanCancelOrder(Order? order) => order is not null && IsValidTransitionPublic(order.Status, OrderStatus.Cancelled);
    public static bool CanDeleteOrder(Order? order) => order is not null && order.Status is not (OrderStatus.Shipped or OrderStatus.Completed);

    [RelayCommand]
    public void BeginEditCompletedOrder()
    {
        if (SelectedOrder is not { Status: OrderStatus.Completed }) return;
        var label = SelectedOrder.OrderNumber ?? $"#{SelectedOrder.Id}";
        var reason = dialogService.RequireReason("Edit Completed Order",
            $"Editing order {label}. Enter a reason for this correction — it will be recorded in the audit trail.");
        if (reason is null) return; // cancelled

        _pendingEditReason = reason;
        IsEditingCompletedOrder = true;
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(CanBeginEditCompletedOrder));
    }

    [RelayCommand]
    public void CancelEditCompletedOrder()
    {
        _pendingEditReason = null;
        IsEditingCompletedOrder = false;
        var id = SelectedOrder?.Id;
        Load(); // discards any staged header edits by reloading fresh POCOs from the DB
        SelectedOrder = Orders.FirstOrDefault(o => o.Id == id);
        RefreshLines(); // explicit: SelectedOrder may be the same reference (no-op set), but staged line edits must still be discarded
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(CanBeginEditCompletedOrder));
        StatusMessage = "Edit cancelled.";
    }

    [RelayCommand]
    public async Task SaveEditedOrder()
    {
        if (SelectedOrder is null || _pendingEditReason is null) return;
        try
        {
            await orderService.EditCompletedOrder(SelectedOrder.Id, SelectedOrder, [.. Lines], _pendingEditReason);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            StatusMessage = ex.Message;
            return; // stay in edit mode so the user can fix the offending change
        }

        _pendingEditReason = null;
        IsEditingCompletedOrder = false;
        var id = SelectedOrder.Id;
        Load();
        SelectedOrder = Orders.FirstOrDefault(o => o.Id == id);
        RefreshLines(); // explicit: SelectedOrder may be the same reference (no-op set) — force the corrected lines to load
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(CanBeginEditCompletedOrder));
        StatusMessage = "Order updated.";
    }

    [RelayCommand]
    public void NewOrder()
    {
        if (SelectedCustomer is null) { StatusMessage = "Pick a customer first."; return; }
        var order = orderService.CreateOrder(SelectedCustomer.Id, SalesChannel.TcgPlayer, null);
        Load();
        SelectedOrder = Orders.FirstOrDefault(o => o.Id == order.Id);
    }

    [RelayCommand]
    public void AddCard()
    {
        if (SelectedOrder is null || SelectedAvailableCard is null) return;
        if (!IsEditable) { StatusMessage = "Only a Created order (or an order in edit mode) can be edited."; return; }

        if (IsEditingCompletedOrder)
        {
            // Staged locally (Id = 0 marks "new" for EditCompletedOrder's diff) — no DB write
            // until SaveEditedOrder commits the whole edit session atomically.
            Lines.Add(new OrderLine
            {
                Id = 0,
                OrderId = SelectedOrder.Id,
                LotId = SelectedAvailableCard.LotId,
                NameSnapshot = SelectedAvailableCard.Name,
                SetSnapshot = SelectedAvailableCard.SetName,
                ConditionSnapshot = SelectedAvailableCard.Condition,
                IsFoilSnapshot = SelectedAvailableCard.IsFoil,
                Quantity = 1,
                UnitSalePrice = SelectedAvailableCard.ListedPrice,
            });
            AvailableCards.Remove(SelectedAvailableCard);
            OnPropertyChanged(nameof(OrderTotal));
            return;
        }

        orderService.AddLine(SelectedOrder.Id, SelectedAvailableCard.LotId, SelectedAvailableCard.ListedPrice);
        RefreshLines();
        AvailableCards.Remove(SelectedAvailableCard);
    }

    [RelayCommand]
    public void RemoveLine(OrderLine? line)
    {
        if (SelectedOrder is null || line is null) return;
        if (!IsEditable) { StatusMessage = "Only a Created order (or an order in edit mode) can be edited."; return; }

        if (IsEditingCompletedOrder)
        {
            Lines.Remove(line);
            OnPropertyChanged(nameof(OrderTotal));
            return;
        }

        orderService.RemoveLine(line.Id);
        RefreshLines();
    }

    /// <summary>Re-announces derived state after an in-place DataGrid cell edit (Qty/Price) commits
    /// during Completed-order edit mode — OrderLine is a plain POCO with no INotifyPropertyChanged,
    /// so nothing else observes the mutation.</summary>
    public void OnLineFieldEdited() => OnPropertyChanged(nameof(OrderTotal));

    [RelayCommand]
    public void SaveOrder()
    {
        if (SelectedOrder is null) return;
        orderService.UpdateOrder(SelectedOrder);
        StatusMessage = "Saved.";
    }

    /// <summary>Applies a drag-drop status move (or programmatic move). Validates the transition;
    /// on Packed→Shipped this runs the existing ship accounting via OrderService.SetStatus.</summary>
    public async Task MoveOrder(Order? order, OrderStatus target)
    {
        if (order is null) return;
        if (order.Status == target) return;
        if (!IsValidTransition(order.Status, target))
        {
            StatusMessage = $"Can't move {order.Status} → {target}.";
            return;
        }
        await orderService.SetStatusAsync(order.Id, target);
        var id = order.Id;
        Load();
        SelectedOrder = Orders.FirstOrDefault(o => o.Id == id);
        StatusMessage = $"Order moved to {target}.";
    }

    [RelayCommand(CanExecute = nameof(CanCancelOrder))]
    public async Task CancelOrder(Order? order)
    {
        if (order is null) return;
        if (!IsValidTransition(order.Status, OrderStatus.Cancelled))
        {
            StatusMessage = $"Can't cancel a {order.Status} order.";
            return;
        }
        await MoveOrder(order, OrderStatus.Cancelled);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteOrder))]
    public void DeleteOrder(Order? order)
    {
        if (order is null) return;
        if (order.Status is OrderStatus.Shipped or OrderStatus.Completed)
        {
            StatusMessage = $"Can't delete a {order.Status} order.";
            return;
        }
        var label = order.OrderNumber ?? $"#{order.Id}";
        if (!dialogService.Confirm($"Delete order {label} and its items? This can't be undone.", "Delete order"))
            return;
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

    [RelayCommand]
    public void PrintReceipt()
    {
        if (SelectedOrder is null) { StatusMessage = "Select an order first."; return; }
        try
        {
            var doc = receiptService.BuildReceipt(SelectedOrder.Id);
            ReceiptPrinter.Print(doc);
        }
        catch (Exception ex)
        {
            // No global unhandled-exception handler (see SalesViewModel.Load/MarkAllPicked) —
            // a printer-driver error or undecodable logo image must not crash the app.
            StatusMessage = $"Print failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ExportPdf()
    {
        if (SelectedOrder is null) { StatusMessage = "Select an order first."; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export receipt PDF",
            Filter = "PDF|*.pdf",
            FileName = $"receipt-{SelectedOrder.OrderNumber ?? SelectedOrder.Id.ToString()}.pdf",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var doc = receiptService.BuildReceipt(SelectedOrder.Id);
            receiptPdfExporter.Export(doc, dialog.FileName);
            StatusMessage = $"Exported to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            // No global unhandled-exception handler (see SalesViewModel.Load/MarkAllPicked) —
            // an undecodable logo image or an unwritable/locked export path must not crash the app.
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ImportTcgPlayer()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import TCGPlayer Shipping Export",
            Filter = "CSV files|*.csv|All files|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var preview = importService.PreviewImport(dialog.FileName);
            if (preview.Rows.Count == 0)
            {
                StatusMessage = preview.Warnings.Count > 0
                    ? preview.Warnings[0]
                    : "No orders found in that file.";
                return;
            }

            var imported = dialogService.ShowTcgOrderImportPreview(preview);
            if (imported > 0)
            {
                Load();
                StatusMessage = $"Imported {imported} order(s).";
            }
            else
            {
                StatusMessage = "Import cancelled.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }

    /// <summary>Enforces the kanban board's move rules: Created ⇄ Packed (un-pack allowed),
    /// Packed → Shipped (runs ship accounting), Shipped → Completed, and Cancel only from
    /// Created or Packed (no restock support once Shipped/Completed).</summary>
    private static bool IsValidTransition(OrderStatus from, OrderStatus to) => to switch
    {
        OrderStatus.Created => from is OrderStatus.Packed,          // un-pack
        OrderStatus.Packed => from is OrderStatus.Created,
        OrderStatus.Shipped => from is OrderStatus.Packed,
        OrderStatus.Completed => from is OrderStatus.Shipped,
        OrderStatus.Cancelled => from is OrderStatus.Created or OrderStatus.Packed,
        _ => false,
    };

    internal static bool IsValidTransitionPublic(OrderStatus from, OrderStatus to) => IsValidTransition(from, to);

    private void RefreshLines()
    {
        Lines.Clear();
        if (SelectedOrder is not null)
            foreach (var l in orderService.GetLines(SelectedOrder.Id)) Lines.Add(l);
        OnPropertyChanged(nameof(OrderTotal));
        OnPropertyChanged(nameof(HasReconciliation));
        OnPropertyChanged(nameof(ReconciliationHint));
    }
}
