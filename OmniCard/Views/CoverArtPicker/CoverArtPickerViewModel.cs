using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Views.CoverArtPicker;

public sealed partial class CoverArtPickerViewModel : ViewModel
{
    private readonly IStorageContainerService _containerService;
    private List<CollectionCard> _allCards = [];

    public CoverArtPickerViewModel(IStorageContainerService containerService)
    {
        _containerService = containerService;
    }

    [ObservableProperty]
    public partial string ContainerName { get; set; } = "";

    [ObservableProperty]
    public partial string FilterText { get; set; } = "";

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    public ObservableCollection<CollectionCard> FilteredCards { get; } = [];

    [ObservableProperty]
    public partial CollectionCard? SelectedCard { get; set; }

    public string? PreviewImageUri => SelectedCard?.ImageUri;

    partial void OnSelectedCardChanged(CollectionCard? value)
        => OnPropertyChanged(nameof(PreviewImageUri));

    public int? SelectedCardId => SelectedCard?.Id;

    public Action<bool?>? CloseDialog { get; set; }

    public void Load(int containerId, string containerName)
    {
        ContainerName = containerName;
        _allCards = _containerService.GetCardsInContainer(containerId);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredCards.Clear();
        var filter = FilterText;
        foreach (var card in _allCards)
        {
            if (string.IsNullOrEmpty(filter)
                || card.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredCards.Add(card);
            }
        }
    }

    [RelayCommand]
    public void Confirm()
    {
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel()
    {
        CloseDialog?.Invoke(null);
    }
}
