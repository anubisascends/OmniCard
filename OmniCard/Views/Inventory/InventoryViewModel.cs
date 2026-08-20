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
    private readonly IUpcLookupService _upcLookupService;
    private readonly IStorageContainerService _containerService;
    private readonly IListingService _listingService;

    public InventoryViewModel(
        IInventoryService inventoryService,
        IDialogService dialogService,
        ISealedPriceUpdateService sealedPriceUpdateService,
        IUpcLookupService upcLookupService,
        IStorageContainerService containerService,
        IListingService listingService)
    {
        _inventoryService = inventoryService;
        _dialogService = dialogService;
        _sealedPriceUpdateService = sealedPriceUpdateService;
        _upcLookupService = upcLookupService;
        _containerService = containerService;
        _listingService = listingService;
    }

    [ObservableProperty]
    public partial bool ShowInventory { get; set; }

    public ObservableCollection<InventoryRow> Rows { get; } = [];

    [ObservableProperty]
    public partial InventoryRow? SelectedRow { get; set; }

    public bool HasSelection => SelectedRow is not null;

    /// <summary>Mirrors the global game selector so the Inventory list shows only the active game's
    /// products (null = All Games). Set via <see cref="SetGame"/> from the root view model, and also
    /// used to seed the game of newly added/scanned products.</summary>
    [ObservableProperty]
    public partial CardGame? GameFilter { get; set; }

    partial void OnSelectedRowChanged(InventoryRow? value)
    {
        EditProductCommand.NotifyCanExecuteChanged();
        AddLotCommand.NotifyCanExecuteChanged();
        OpenUnitsCommand.NotifyCanExecuteChanged();
        DeleteProductCommand.NotifyCanExecuteChanged();
    }

    // Header totals — summed from the sealed-only rows built in LoadInventory (not
    // IInventoryService.GetValuation, which also includes singles).
    [ObservableProperty]
    public partial int TotalUnits { get; set; }

    [ObservableProperty]
    public partial decimal TotalCost { get; set; }

    [ObservableProperty]
    public partial decimal TotalMarket { get; set; }

    /// <summary>Set by RootViewModel to report status messages.</summary>
    public Action<string>? ReportMessage { get; set; }

    /// <summary>Set by the view to return keyboard focus to the barcode-scan box after each scan.</summary>
    public Action? FocusScanBox { get; set; }

    /// <summary>Bound to the barcode-scan TextBox in the toolbar. A hardware scanner types the
    /// UPC here and sends Enter, which fires <see cref="ScanUpcCommand"/>.</summary>
    [ObservableProperty]
    public partial string ScanUpc { get; set; } = "";

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshingSealedPrices { get; set; }

    partial void OnShowInventoryChanged(bool value)
    {
        if (value)
            LoadInventory();
    }

    /// <summary>Applies the active game filter and reloads. Called by RootViewModel when the global
    /// game selector changes.</summary>
    public void SetGame(CardGame? game)
    {
        GameFilter = game;
        LoadInventory();
    }

    public void LoadInventory()
    {
        var previousProductId = SelectedRow?.Product.Id;
        Rows.Clear();

        var totalUnits = 0;
        var totalCost = 0m;
        var totalMarket = 0m;

        // Resolve storage-location names once for the lot sub-rows.
        var locationNames = _containerService.GetAll().ToDictionary(c => c.Id, c => c.Name);

        // Active on-market status per lot (Listed/Picked), so each lot can show a state pill like
        // the card tiles do. A lot has at most one active listing; if more, prefer the more
        // advanced status (Picked > Listed).
        var listingByLot = _listingService.GetListingDetails()
            .GroupBy(d => d.LotId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.Status).First());

        // The Inventory tab is scoped to sealed product (singles live in the Collection tab,
        // which prices them via the live per-card game service rather than Product.MarketPrice).
        // GameFilter (mirroring the global game selector) narrows the list to one game; null = all.
        foreach (var product in _inventoryService.GetProducts(GameFilter).Where(p => p.Category != ProductCategory.Single))
        {
            var lots = _inventoryService.GetLots(product.Id);
            var qty = lots.Sum(l => l.Quantity);
            var cost = lots.Sum(l => l.Quantity * (l.UnitCost ?? 0m));
            // Sealed products (Task 1, Phase 3) are priced via the persisted eBay-derived
            // LastMarketPrice.
            var market = qty * (product.LastMarketPrice ?? 0m);

            var lotRows = lots.Select(l => new LotRow(
                l,
                l.LocationId is int locId && locationNames.TryGetValue(locId, out var name) ? name : null)
            {
                ListingStatus = listingByLot.TryGetValue(l.Id, out var d) ? d.Status : null,
                ListingChannel = listingByLot.TryGetValue(l.Id, out var d2) ? d2.Channel : null,
            });

            Rows.Add(new InventoryRow(product, qty, cost, market, lotRows));

            totalUnits += qty;
            totalCost += cost;
            totalMarket += market;
        }

        TotalUnits = totalUnits;
        TotalCost = totalCost;
        TotalMarket = totalMarket;

        // Keep the selected row's identity across a reload, if it still exists.
        if (previousProductId is int id)
            SelectedRow = Rows.FirstOrDefault(r => r.Product.Id == id);
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

    /// <summary>
    /// Barcode-scan entry point. Reads the UPC that a hardware scanner typed into the scan box:
    ///  - Known UPC → jump straight to the "what did you pay?" add-lot dialog for that product.
    ///  - Unknown UPC → silently look the barcode up online, prefill a new sealed product from
    ///    whatever was found, let the user confirm/complete it, then add its first lot.
    /// The game filter is irrelevant to the lookup (that's purely by UPC across all games), but a
    /// non-"All Games" filter seeds the new product's game so it lands in the right game.
    /// </summary>
    [RelayCommand]
    public async Task ScanUpcAsync()
    {
        var upc = (ScanUpc ?? "").Trim();
        ScanUpc = "";

        if (upc.Length == 0)
        {
            FocusScanBox?.Invoke();
            return;
        }

        var existing = _inventoryService.FindProductByUpc(upc);
        if (existing is not null)
        {
            ReportMessage?.Invoke($"Found '{existing.Name}' — add what you paid.");
            AddLotFor(existing);
            FocusScanBox?.Invoke();
            return;
        }

        // Unknown UPC: attempt a silent online lookup so the editor opens as prefilled as possible.
        UpcLookupResult? info = null;
        IsScanning = true;
        ReportMessage?.Invoke($"Looking up UPC {upc}…");
        try
        {
            info = await _upcLookupService.LookupAsync(upc);
        }
        finally
        {
            IsScanning = false;
        }

        ReportMessage?.Invoke(info?.Title is { Length: > 0 } title
            ? $"Found \"{title}\" — confirm the details."
            : $"No online match for UPC {upc} — enter the details manually.");

        var prefilled = new Product
        {
            Game = GameFilter ?? CardGame.Mtg,
            Category = ProductCategory.Box,
            Name = info?.Title ?? "",
            Upc = upc,
            ImageUri = info?.ImageUrl,
        };

        var created = _dialogService.EditProduct(prefilled);
        if (created is null)
        {
            FocusScanBox?.Invoke();
            return;
        }

        var saved = _inventoryService.CreateProduct(created);
        ReportMessage?.Invoke($"Added product '{saved.Name}'.");
        AddLotFor(saved);
        LoadInventory();
        FocusScanBox?.Invoke();
    }

    /// <summary>Prompt for cost/quantity/location and record a lot for <paramref name="product"/>.</summary>
    private void AddLotFor(Product product)
    {
        var input = _dialogService.AddLotDialog(product.Id);
        if (input is null) return;

        var (quantity, unitCost, locationId, source, date) = input.Value;
        _inventoryService.AddLot(product.Id, quantity, unitCost, locationId, source, date);

        ReportMessage?.Invoke($"Added {quantity} unit(s) of '{product.Name}'.");
        LoadInventory();
    }

    [RelayCommand]
    public void AddProduct()
    {
        // Seed the new product's game from the active filter (still editable in the dialog); when
        // "All Games" is selected there's nothing to seed, so the editor uses its own default.
        var seed = GameFilter is CardGame g ? new Product { Game = g, Category = ProductCategory.Box } : null;

        var product = _dialogService.EditProduct(seed);
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
        AddLotFor(SelectedRow.Product);
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

    // ----- Per-lot actions (invoked from the expandable lot rows) -----

    /// <summary>Edit a single lot's quantity/cost/location/source/date. Every other field on the
    /// lot is preserved.</summary>
    [RelayCommand]
    public void EditLot(LotRow? row)
    {
        if (row is null) return;

        var input = _dialogService.EditLotDialog(row.Lot);
        if (input is null) return;

        var (quantity, unitCost, locationId, source, date) = input.Value;
        var lot = row.Lot;
        lot.Quantity = quantity;
        lot.UnitCost = unitCost;
        lot.LocationId = locationId;
        lot.Source = source;
        lot.AcquisitionDate = date;

        _inventoryService.UpdateLot(lot);
        ReportMessage?.Invoke($"Updated lot for '{lot.Product?.Name ?? ProductNameFor(lot.ProductId)}'.");
        LoadInventory();
    }

    [RelayCommand]
    public void DeleteLot(LotRow? row)
    {
        if (row is null) return;

        var confirm = MessageBox.Show(
            $"Delete this lot ({row.Quantity} unit(s))? This cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        _inventoryService.DeleteLot(row.LotId);
        ReportMessage?.Invoke("Deleted lot.");
        LoadInventory();
    }

    /// <summary>Quick "change storage location" for a single lot, reusing the shared location picker.</summary>
    [RelayCommand]
    public void MoveLot(LotRow? row)
    {
        if (row is null) return;

        var result = _dialogService.PickMoveToLocation();
        if (result is null) return;

        var lot = row.Lot;
        lot.LocationId = result.Container.Id;
        _inventoryService.UpdateLot(lot);
        ReportMessage?.Invoke($"Moved lot to '{result.Container.Name}'.");
        LoadInventory();
    }

    /// <summary>Open sealed units from a specific lot (vs. the product-level Open Units command,
    /// which lets the user pick the lot).</summary>
    [RelayCommand]
    public void OpenUnitsForLot(LotRow? row)
    {
        if (row is null) return;

        var product = _inventoryService.GetProducts().FirstOrDefault(p => p.Id == row.Lot.ProductId);
        if (product is null) return;

        if (_dialogService.OpenUnitsDialog(product, row.LotId))
        {
            ReportMessage?.Invoke($"Opened units of '{product.Name}'.");
            LoadInventory();
        }
    }

    /// <summary>List a single lot for sale on a generic channel (Manual / TCGPlayer), mirroring the
    /// collection view's List-for-Sale flow. eBay has its own richer flow — see <see cref="ListLotOnEbay"/>.</summary>
    [RelayCommand]
    public void ListLotForSale(LotRow? row)
    {
        if (row is null) return;

        var suggested = Rows.FirstOrDefault(r => r.Lots.Contains(row))?.Product.MarketPrice
                        ?? row.Lot.UnitCost ?? 0m;
        var result = _dialogService.PickListForSale(suggested);
        if (result is null) return;

        if (result.Quantity <= 0 || result.Price < 0)
        {
            ReportMessage?.Invoke("Enter a positive quantity and non-negative price.");
            return;
        }

        var count = _listingService.ListForSale([row.LotId], result.Channel, result.Price, result.Quantity);
        ReportMessage?.Invoke(count > 0 ? "Listed lot for sale." : "This lot is already listed.");
        LoadInventory();
    }

    /// <summary>List a single sealed lot on eBay via the (sealed-aware) eBay listing dialog.</summary>
    [RelayCommand]
    public void ListLotOnEbay(LotRow? row)
    {
        if (row is null) return;

        var product = _inventoryService.GetProducts().FirstOrDefault(p => p.Id == row.Lot.ProductId);
        if (product is null) return;

        if (_dialogService.OpenEbayListingDialog(product, row.LotId, product.MarketPrice) == true)
        {
            ReportMessage?.Invoke($"Listed \"{product.Name}\" on eBay.");
            LoadInventory();
        }
    }

    private string ProductNameFor(int productId) =>
        _inventoryService.GetProducts().FirstOrDefault(p => p.Id == productId)?.Name ?? "product";
}
