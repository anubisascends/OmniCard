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
    IListingService listingService) : ObservableObject
{
    public ObservableCollection<Order> Orders { get; } = [];
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

    /// <summary>Loads orders, customers and active listings. Safe to call repeatedly (e.g. on
    /// every tab activation).</summary>
    public void Load()
    {
        Orders.Clear();
        foreach (var o in orderService.GetOrders()) Orders.Add(o);
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
        if (SelectedOrder.Status != OrderStatus.Open) { StatusMessage = "Can only edit an Open order."; return; }
        orderService.AddLine(SelectedOrder.Id, SelectedAvailableCard.LotId, SelectedAvailableCard.ListedPrice);
        RefreshLines();
        AvailableCards.Remove(SelectedAvailableCard);
    }

    [RelayCommand]
    public void RemoveLine(OrderLine? line)
    {
        if (SelectedOrder is null || line is null) return;
        if (SelectedOrder.Status != OrderStatus.Open) { StatusMessage = "Can only edit an Open order."; return; }
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

    [RelayCommand]
    public void SetStatus(OrderStatus status)
    {
        if (SelectedOrder is null) return;
        orderService.SetStatus(SelectedOrder.Id, status);
        var id = SelectedOrder.Id;
        Load();
        SelectedOrder = Orders.FirstOrDefault(o => o.Id == id);
        StatusMessage = $"Order marked {status}.";
    }

    private void RefreshLines()
    {
        Lines.Clear();
        if (SelectedOrder is not null)
            foreach (var l in orderService.GetLines(SelectedOrder.Id)) Lines.Add(l);
        OnPropertyChanged(nameof(OrderTotal));
    }
}
