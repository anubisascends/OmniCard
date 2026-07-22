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
    IDialogService dialogService) : ObservableObject
{
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

    public decimal OrderTotal => Lines.Sum(l => l.Quantity * l.UnitSalePrice);

    public bool IsEditable => SelectedOrder?.Status == OrderStatus.Created;

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

    partial void OnSelectedOrderChanged(Order? value)
    {
        Lines.Clear();
        if (value is not null)
            foreach (var l in orderService.GetLines(value.Id)) Lines.Add(l);
        SelectedCustomer = value is null ? null : Customers.FirstOrDefault(c => c.Id == value.CustomerId);
        OnPropertyChanged(nameof(OrderTotal));
        OnPropertyChanged(nameof(HasReconciliation));
        OnPropertyChanged(nameof(ReconciliationHint));
        OnPropertyChanged(nameof(IsEditable));
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
        if (!IsEditable) { StatusMessage = "Only a Created order can be edited."; return; }
        orderService.AddLine(SelectedOrder.Id, SelectedAvailableCard.LotId, SelectedAvailableCard.ListedPrice);
        RefreshLines();
        AvailableCards.Remove(SelectedAvailableCard);
    }

    [RelayCommand]
    public void RemoveLine(OrderLine? line)
    {
        if (SelectedOrder is null || line is null) return;
        if (!IsEditable) { StatusMessage = "Only a Created order can be edited."; return; }
        orderService.RemoveLine(line.Id);
        RefreshLines();
    }

    [RelayCommand]
    public void SaveOrder()
    {
        if (SelectedOrder is null) return;
        orderService.UpdateOrder(SelectedOrder);
        StatusMessage = "Saved.";
    }

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
