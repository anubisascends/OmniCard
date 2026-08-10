using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.MoveListToLocation;

public sealed partial class MoveListToLocationViewModel(IStorageContainerService containerService) : ViewModel
{
    public ObservableCollection<StorageContainer> AvailableLocations { get; } = [];
    public IReadOnlyList<ContainerType> LocationTypes { get; } = Enum.GetValues<ContainerType>();
    public IReadOnlyList<string> ConditionOptions { get; } = ["NM", "LP", "MP", "HP", "D"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    public partial StorageContainer? SelectedLocation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseExistingLocation))]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    public partial bool CreateNew { get; set; }

    public bool UseExistingLocation => !CreateNew;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    public partial string NewName { get; set; } = "";

    [ObservableProperty]
    public partial ContainerType NewLocationType { get; set; } = ContainerType.Box;

    [ObservableProperty]
    public partial string Condition { get; set; } = "NM";

    public bool CanConfirm => CreateNew ? !string.IsNullOrWhiteSpace(NewName) : SelectedLocation is not null;

    public Action<bool>? CloseDialog { get; set; }

    public MoveListToLocationResult? Result { get; private set; }

    public void Load()
    {
        AvailableLocations.Clear();
        foreach (var c in containerService.GetAll()) AvailableLocations.Add(c);
        SelectedLocation = null;
        CreateNew = false;
        NewName = "";
        NewLocationType = ContainerType.Box;
        Condition = "NM";
        Result = null;
    }

    [RelayCommand]
    private void Confirm()
    {
        if (!CanConfirm) return;
        Result = new MoveListToLocationResult
        {
            ExistingContainer = CreateNew ? null : SelectedLocation,
            CreateNew = CreateNew,
            NewContainerName = NewName.Trim(),
            NewContainerType = NewLocationType,
            Condition = Condition,
        };
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseDialog?.Invoke(false);
}
