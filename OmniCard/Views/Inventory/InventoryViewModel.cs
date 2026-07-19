using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;

namespace OmniCard.Views.Inventory;

public sealed partial class InventoryViewModel : ViewModel
{
    private readonly IInventoryService _inventoryService;
    private readonly IDialogService _dialogService;

    public InventoryViewModel(IInventoryService inventoryService, IDialogService dialogService)
    {
        _inventoryService = inventoryService;
        _dialogService = dialogService;
    }

    [ObservableProperty]
    public partial bool ShowInventory { get; set; }

    public ObservableCollection<InventoryRow> Rows { get; } = [];

    [ObservableProperty]
    public partial InventoryRow? SelectedRow { get; set; }

    // Header totals (from IInventoryService.GetValuation)
    [ObservableProperty]
    public partial int TotalUnits { get; set; }

    [ObservableProperty]
    public partial decimal TotalCost { get; set; }

    [ObservableProperty]
    public partial decimal TotalMarket { get; set; }

    /// <summary>Set by RootViewModel to report status messages.</summary>
    public Action<string>? ReportMessage { get; set; }

    partial void OnShowInventoryChanged(bool value)
    {
        if (value)
            LoadInventory();
    }

    public void LoadInventory()
    {
        Rows.Clear();
        foreach (var product in _inventoryService.GetProducts())
        {
            var lots = _inventoryService.GetLots(product.Id);
            var qty = lots.Sum(l => l.Quantity);
            var cost = lots.Sum(l => l.Quantity * (l.UnitCost ?? 0m));
            var market = qty * product.MarketPrice;
            Rows.Add(new InventoryRow(product, qty, cost, market));
        }

        var valuation = _inventoryService.GetValuation();
        TotalUnits = valuation.TotalUnits;
        TotalCost = valuation.TotalCost;
        TotalMarket = valuation.TotalMarket;

        // Keep the selected row's identity across a reload, if it still exists.
        if (SelectedRow is not null)
            SelectedRow = Rows.FirstOrDefault(r => r.Product.Id == SelectedRow.Product.Id);
    }

    [RelayCommand]
    public void RefreshInventory() => LoadInventory();

    [RelayCommand]
    public void AddProduct()
    {
        var product = _dialogService.EditProduct(null);
        if (product is null) return;

        _inventoryService.CreateProduct(product);
        ReportMessage?.Invoke($"Added product '{product.Name}'.");
        LoadInventory();
    }

    [RelayCommand]
    public void EditProduct()
    {
        if (SelectedRow is null) return;

        var updated = _dialogService.EditProduct(SelectedRow.Product);
        if (updated is null) return;

        _inventoryService.UpdateProduct(updated);
        ReportMessage?.Invoke($"Updated '{updated.Name}'.");
        LoadInventory();
    }

    [RelayCommand]
    public void AddLot()
    {
        if (SelectedRow is null) return;

        var input = _dialogService.AddLotDialog(SelectedRow.Product.Id);
        if (input is null) return;

        var (quantity, unitCost, locationId, source, date) = input.Value;
        var lot = _inventoryService.AddLot(SelectedRow.Product.Id, quantity, unitCost, locationId, source);

        // AddLot doesn't accept an acquisition date, so apply it as a follow-up update if the
        // user picked something other than "today" (the lot's default AcquisitionDate).
        if (date.Date != lot.AcquisitionDate.Date)
        {
            lot.AcquisitionDate = date;
            _inventoryService.UpdateLot(lot);
        }

        ReportMessage?.Invoke($"Added {quantity} unit(s) of '{SelectedRow.Product.Name}'.");
        LoadInventory();
    }

    [RelayCommand]
    public void OpenUnits()
    {
        if (SelectedRow is null) return;

        if (_dialogService.OpenUnitsDialog(SelectedRow.Product))
        {
            ReportMessage?.Invoke($"Opened units of '{SelectedRow.Product.Name}'.");
            LoadInventory();
        }
    }
}
