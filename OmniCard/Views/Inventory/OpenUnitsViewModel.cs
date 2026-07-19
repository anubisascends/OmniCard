using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Inventory;

/// <summary>
/// Picks a lot to open units from for a given product. Not part of the original task file list,
/// but required so the InventoryViewModel.OpenUnits command (which needs a lotId + quantity) has
/// a concrete UI — mirrors the AddLotView/ProductEditorView dialog pattern.
/// </summary>
public sealed partial class OpenUnitsViewModel(IInventoryService inventoryService) : ViewModel
{
    public ObservableCollection<InventoryLot> Lots { get; } = [];

    [ObservableProperty]
    public partial string ProductName { get; set; } = "";

    [ObservableProperty]
    public partial InventoryLot? SelectedLot { get; set; }

    [ObservableProperty]
    public partial int Quantity { get; set; } = 1;

    [ObservableProperty]
    public partial string? Note { get; set; }

    [ObservableProperty]
    public partial string? ValidationMessage { get; set; }

    public Action<bool?>? CloseDialog { get; set; }

    public bool WasOpened { get; private set; }

    public void Load(Product product)
    {
        ProductName = product.Name;
        Lots.Clear();
        foreach (var lot in inventoryService.GetLots(product.Id))
            Lots.Add(lot);
        SelectedLot = Lots.FirstOrDefault();
        Quantity = 1;
        Note = null;
        ValidationMessage = null;
        WasOpened = false;
    }

    [RelayCommand]
    public void Confirm()
    {
        if (SelectedLot is null)
        {
            ValidationMessage = "Select a lot to open units from.";
            return;
        }

        if (Quantity <= 0 || Quantity > SelectedLot.Quantity)
        {
            ValidationMessage = $"Quantity must be between 1 and {SelectedLot.Quantity}.";
            return;
        }

        inventoryService.OpenUnits(SelectedLot.Id, Quantity, string.IsNullOrWhiteSpace(Note) ? null : Note.Trim());
        WasOpened = true;
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}
