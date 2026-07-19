using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Inventory;

public sealed partial class AddLotViewModel(IStorageContainerService containerService, IInventoryService inventoryService) : ViewModel
{
    public ObservableCollection<StorageContainer> AvailableContainers { get; } = [];

    [ObservableProperty]
    public partial string ProductName { get; set; } = "";

    [ObservableProperty]
    public partial int Quantity { get; set; } = 1;

    [ObservableProperty]
    public partial decimal? UnitCost { get; set; }

    [ObservableProperty]
    public partial StorageContainer? SelectedContainer { get; set; }

    [ObservableProperty]
    public partial string? Source { get; set; }

    [ObservableProperty]
    public partial DateTime AcquisitionDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial string? ValidationMessage { get; set; }

    public Action<bool?>? CloseDialog { get; set; }

    public (int Quantity, decimal? UnitCost, int? LocationId, string? Source, DateTime AcquisitionDate)? Result { get; private set; }

    public void Load(int productId)
    {
        ProductName = inventoryService.GetProducts().FirstOrDefault(p => p.Id == productId)?.Name ?? "";
        Quantity = 1;
        UnitCost = null;
        Source = null;
        AcquisitionDate = DateTime.Today;
        ValidationMessage = null;

        AvailableContainers.Clear();
        foreach (var c in containerService.GetAll())
            AvailableContainers.Add(c);
        SelectedContainer = null;
    }

    [RelayCommand]
    public void Confirm()
    {
        if (Quantity <= 0)
        {
            ValidationMessage = "Quantity must be greater than zero.";
            return;
        }

        Result = (Quantity, UnitCost, SelectedContainer?.Id,
            string.IsNullOrWhiteSpace(Source) ? null : Source.Trim(), AcquisitionDate);
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}
