using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views;

namespace OmniCard.Views.ManualAdd;

public sealed partial class ManualAddViewModel : ViewModel
{
    private readonly ICardService _cardService;
    private readonly IStorageContainerService _containerService;
    private readonly ILogger<ManualAddViewModel> _logger;

    public ManualAddViewModel(
        ICardService cardService,
        IStorageContainerService containerService,
        ILogger<ManualAddViewModel> logger)
    {
        _cardService = cardService;
        _containerService = containerService;
        _logger = logger;
    }

    public Action<bool?>? CloseDialog { get; set; }

    // Search
    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    public ObservableCollection<CardMatch> SearchResults { get; } = [];

    [ObservableProperty]
    public partial CardMatch? SelectedResult { get; set; }

    // Card properties
    [ObservableProperty]
    public partial string Condition { get; set; } = "NM";

    [ObservableProperty]
    public partial bool IsFoil { get; set; }

    [ObservableProperty]
    public partial decimal? PurchasePrice { get; set; }

    [ObservableProperty]
    public partial int Quantity { get; set; } = 1;

    // Location
    public ObservableCollection<StorageContainer> Containers { get; } = [];

    [ObservableProperty]
    public partial StorageContainer? SelectedContainer { get; set; }

    [ObservableProperty]
    public partial int? Page { get; set; }

    [ObservableProperty]
    public partial int? Slot { get; set; }

    [ObservableProperty]
    public partial string? Section { get; set; }

    // Status
    [ObservableProperty]
    public partial int AddedCount { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    public bool HasAdded => AddedCount > 0;

    public void Load(StorageContainer? defaultContainer = null)
    {
        var containers = _containerService.GetAll();
        Containers.Clear();
        foreach (var c in containers)
            Containers.Add(c);

        SelectedContainer = defaultContainer ?? (Containers.Count > 0 ? Containers[0] : null);
    }

    [RelayCommand]
    public void Search()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        var game = _cardService.SelectedGame;
        var gameService = _cardService.GetGameService(game);
        var results = gameService.SearchCards(SearchQuery, 20);

        SearchResults.Clear();
        foreach (var r in results)
            SearchResults.Add(r);

        if (SearchResults.Count > 0)
            SelectedResult = SearchResults[0];

        StatusMessage = SearchResults.Count == 0 ? "No cards found." : "";
    }

    [RelayCommand]
    public void AddToCollection()
    {
        if (SelectedResult is null)
        {
            StatusMessage = "Select a card first.";
            return;
        }

        var game = _cardService.SelectedGame;
        _cardService.AddCardToCollection(
            SelectedResult,
            game,
            Condition,
            IsFoil,
            PurchasePrice,
            Quantity,
            SelectedContainer,
            Page,
            Slot,
            Section);

        AddedCount += Quantity;
        OnPropertyChanged(nameof(HasAdded));
        StatusMessage = $"{AddedCount} card{(AddedCount == 1 ? "" : "s")} added";
        _logger.LogInformation("Manually added {Qty}x {Name} to collection", Quantity, SelectedResult.Name);

        // Reset for next card
        SearchQuery = "";
        SearchResults.Clear();
        SelectedResult = null;
        Quantity = 1;
    }
}
