using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Inventory;

public sealed partial class InventoryViewModel : ViewModel
{
    private readonly IInventoryService _inventoryService;
    private readonly IDialogService _dialogService;
    private readonly ISealedPriceUpdateService _sealedPriceUpdateService;

    public InventoryViewModel(
        IInventoryService inventoryService,
        IDialogService dialogService,
        ISealedPriceUpdateService sealedPriceUpdateService)
    {
        _inventoryService = inventoryService;
        _dialogService = dialogService;
        _sealedPriceUpdateService = sealedPriceUpdateService;
    }

    [ObservableProperty]
    public partial bool ShowInventory { get; set; }

    public ObservableCollection<InventoryRow> Rows { get; } = [];

    [ObservableProperty]
    public partial InventoryRow? SelectedRow { get; set; }

    public bool HasSelection => SelectedRow is not null;

    partial void OnSelectedRowChanged(InventoryRow? value)
    {
        EditProductCommand.NotifyCanExecuteChanged();
        AddLotCommand.NotifyCanExecuteChanged();
        OpenUnitsCommand.NotifyCanExecuteChanged();
        DeleteProductCommand.NotifyCanExecuteChanged();
    }

    // Header totals (from IInventoryService.GetValuation)
    [ObservableProperty]
    public partial int TotalUnits { get; set; }

    [ObservableProperty]
    public partial decimal TotalCost { get; set; }

    [ObservableProperty]
    public partial decimal TotalMarket { get; set; }

    /// <summary>Set by RootViewModel to report status messages.</summary>
    public Action<string>? ReportMessage { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshingSealedPrices { get; set; }

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
            // Sealed products (Task 1, Phase 3) are priced via the persisted eBay-derived
            // LastMarketPrice; singles keep the existing (manually/live-set) MarketPrice.
            var market = qty * (product.Category == ProductCategory.Single
                ? product.MarketPrice
                : (product.LastMarketPrice ?? 0m));
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

    public bool CanRefreshSealedPrices => !IsRefreshingSealedPrices;

    partial void OnIsRefreshingSealedPricesChanged(bool value) => RefreshSealedPricesCommand.NotifyCanExecuteChanged();

    /// <summary>Task 1 (Phase 3): manual trigger for automated sealed pricing via eBay median.
    /// Ignores any cooldown — this is an explicit, user-initiated refresh.</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshSealedPrices))]
    public async Task RefreshSealedPricesAsync()
    {
        IsRefreshingSealedPrices = true;
        ReportMessage?.Invoke("Refreshing sealed product prices...");
        try
        {
            var progress = new Progress<PriceUpdateProgress>(p => ReportMessage?.Invoke(p.Message));
            var updated = await _sealedPriceUpdateService.RefreshSealedPricesAsync(progress);
            ReportMessage?.Invoke(updated > 0
                ? $"Updated market price for {updated} sealed product(s)."
                : "No sealed product prices were updated.");
            LoadInventory();
        }
        finally
        {
            IsRefreshingSealedPrices = false;
        }
    }

    [RelayCommand]
    public void AddProduct()
    {
        var product = _dialogService.EditProduct(null);
        if (product is null) return;

        _inventoryService.CreateProduct(product);
        ReportMessage?.Invoke($"Added product '{product.Name}'.");
        LoadInventory();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void EditProduct()
    {
        if (SelectedRow is null) return;

        var updated = _dialogService.EditProduct(SelectedRow.Product);
        if (updated is null) return;

        _inventoryService.UpdateProduct(updated);
        ReportMessage?.Invoke($"Updated '{updated.Name}'.");
        LoadInventory();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void AddLot()
    {
        if (SelectedRow is null) return;

        var input = _dialogService.AddLotDialog(SelectedRow.Product.Id);
        if (input is null) return;

        var (quantity, unitCost, locationId, source, date) = input.Value;
        _inventoryService.AddLot(SelectedRow.Product.Id, quantity, unitCost, locationId, source, date);

        ReportMessage?.Invoke($"Added {quantity} unit(s) of '{SelectedRow.Product.Name}'.");
        LoadInventory();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void OpenUnits()
    {
        if (SelectedRow is null) return;

        if (_dialogService.OpenUnitsDialog(SelectedRow.Product))
        {
            ReportMessage?.Invoke($"Opened units of '{SelectedRow.Product.Name}'.");
            LoadInventory();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void DeleteProduct()
    {
        if (SelectedRow is null) return;

        var product = SelectedRow.Product;
        var confirm = MessageBox.Show(
            $"Delete '{product.Name}' and all of its lots? This cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        _inventoryService.DeleteProduct(product.Id);
        ReportMessage?.Invoke($"Deleted '{product.Name}'.");
        LoadInventory();
    }
}
