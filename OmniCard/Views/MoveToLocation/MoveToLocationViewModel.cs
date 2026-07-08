using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.MoveToLocation;

public sealed partial class MoveToLocationViewModel(IStorageContainerService containerService) : ViewModel
{
    // All containers grouped by type
    public ObservableCollection<StorageContainer> Binders { get; } = [];
    public ObservableCollection<StorageContainer> Boxes { get; } = [];
    public ObservableCollection<StorageContainer> DeckBoxes { get; } = [];
    public ObservableCollection<StorageContainer> DisplayCases { get; } = [];
    public ObservableCollection<StorageContainer> BulkContainers { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial StorageContainer? SelectedContainer { get; set; }

    [ObservableProperty]
    public partial string? Section { get; set; }

    public bool ShowBoxFields => SelectedContainer?.ContainerType == ContainerType.Box;

    partial void OnSelectedContainerChanged(StorageContainer? value)
    {
        if (value?.ContainerType != ContainerType.Box)
            Section = null;
        OnPropertyChanged(nameof(ShowBoxFields));
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterContainers();
    }

    public Action<bool>? CloseDialog { get; set; }

    public MoveToLocationResult? Result { get; private set; }

    public void Load()
    {
        _allContainers = containerService.GetAll().ToList();
        FilterContainers();
    }

    private List<StorageContainer> _allContainers = [];

    private void FilterContainers()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allContainers
            : _allContainers.Where(c => c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

        Binders.Clear();
        Boxes.Clear();
        DeckBoxes.Clear();
        DisplayCases.Clear();
        BulkContainers.Clear();

        foreach (var c in filtered)
        {
            var target = c.ContainerType switch
            {
                ContainerType.Binder => Binders,
                ContainerType.Box => Boxes,
                ContainerType.DeckBox => DeckBoxes,
                ContainerType.DisplayCase => DisplayCases,
                ContainerType.Bulk => BulkContainers,
                _ => BulkContainers,
            };
            target.Add(c);
        }
    }

    [RelayCommand]
    public void Confirm()
    {
        if (SelectedContainer is null) return;
        Result = new MoveToLocationResult
        {
            Container = SelectedContainer,
            Section = SelectedContainer.ContainerType == ContainerType.Box ? Section : null,
        };
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}
