using System.Collections.ObjectModel;
using System.Linq;
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

    /// <summary>Sets available for the active game, with an "All Sets" sentinel (empty
    /// <see cref="SetInfo.SetCode"/>) first so the user can opt out of set filtering.</summary>
    public ObservableCollection<SetInfo> AvailableSets { get; } = [];

    /// <summary>Set to restrict the search to; the "All Sets" sentinel means no restriction.</summary>
    [ObservableProperty]
    public partial SetInfo? SelectedSet { get; set; }

    /// <summary>Optional exact collector number to search for.</summary>
    [ObservableProperty]
    public partial string CollectorNumber { get; set; } = "";

    // Card properties
    [ObservableProperty]
    public partial string Condition { get; set; } = "NM";

    [ObservableProperty]
    public partial bool IsFoil { get; set; }

    /// <summary>Finish presets for the active game, offered when <see cref="IsFoil"/> is on.</summary>
    public ObservableCollection<string> AvailableFoilTypes { get; } = [];

    /// <summary>Selected foil finish; only meaningful when <see cref="IsFoil"/> is on.</summary>
    [ObservableProperty]
    public partial string? FoilType { get; set; }

    // Seed a sensible finish when foil is first checked; clear it when unchecked.
    partial void OnIsFoilChanged(bool value)
        => FoilType = value ? (FoilType ?? AvailableFoilTypes.FirstOrDefault()) : null;

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

    /// <summary>True when the dialog is locked to a single binder slot (opened via "Add Missing
    /// Card..."). Container/Page/Slot are fixed and the add goes straight into that slot with swap
    /// semantics; the dialog closes after one card since a slot holds exactly one.</summary>
    [ObservableProperty]
    public partial bool IsSlotLocked { get; set; }

    /// <summary>Inverse of <see cref="IsSlotLocked"/> for enabling the location/quantity fields.</summary>
    public bool IsSlotEditable => !IsSlotLocked;

    partial void OnIsSlotLockedChanged(bool value) => OnPropertyChanged(nameof(IsSlotEditable));

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

        var game = _cardService.SelectedGame;

        AvailableFoilTypes.Clear();
        foreach (var t in FoilTypes.ForGame(game))
            AvailableFoilTypes.Add(t);

        AvailableSets.Clear();
        AvailableSets.Add(new SetInfo("", "All Sets"));
        foreach (var s in _cardService.GetGameService(game).GetAvailableSets())
            AvailableSets.Add(s);
        SelectedSet = AvailableSets[0];
        CollectorNumber = "";

        SelectedContainer = defaultContainer ?? (Containers.Count > 0 ? Containers[0] : null);
    }

    /// <summary>Opens the dialog locked to a specific binder page/slot — used by "Add Missing Card...".</summary>
    public void LoadForSlot(int containerId, int page, int slot)
    {
        Load();
        SelectedContainer = Containers.FirstOrDefault(c => c.Id == containerId);
        Page = page;
        Slot = slot;
        Quantity = 1;
        IsSlotLocked = true;
    }

    [RelayCommand]
    public void Search()
    {
        // Compose the free-text query with the Set and Collector # filters using the
        // set:/cn: token syntax every ICardGameService.SearchCards already understands.
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(SearchQuery))
            parts.Add(SearchQuery.Trim());
        if (SelectedSet is { SetCode.Length: > 0 } set)
            parts.Add($"set:{set.SetCode}");
        if (!string.IsNullOrWhiteSpace(CollectorNumber))
            parts.Add($"cn:{CollectorNumber.Trim()}");

        var query = string.Join(' ', parts);
        if (string.IsNullOrWhiteSpace(query))
        {
            StatusMessage = "Enter a name, set, or collector number to search.";
            return;
        }

        var game = _cardService.SelectedGame;
        var gameService = _cardService.GetGameService(game);
        var results = gameService.SearchCards(query, 20);

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
        var foilType = IsFoil ? (FoilType ?? FoilTypes.BasicFoilType(game)) : null;

        // Slot-locked mode: place directly into the clicked binder slot (swap-aware), one card only.
        if (IsSlotLocked && SelectedContainer is not null && Page is int p && Slot is int s)
        {
            _cardService.AddMissingCardToSlot(SelectedResult, game, Condition, IsFoil, foilType, PurchasePrice,
                SelectedContainer.Id, p, s);
            AddedCount += 1;
            OnPropertyChanged(nameof(HasAdded));
            _logger.LogInformation("Added missing card {Name} to {Container} page {Page} slot {Slot}",
                SelectedResult.Name, SelectedContainer.Id, p, s);
            CloseDialog?.Invoke(true);
            return;
        }

        _cardService.AddCardToCollection(
            SelectedResult,
            game,
            Condition,
            IsFoil,
            foilType,
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

        // Reset for next card. Keep the Set filter — users often add several from one set —
        // but clear the collector number since it identifies a single card.
        SearchQuery = "";
        CollectorNumber = "";
        SearchResults.Clear();
        SelectedResult = null;
        Quantity = 1;
    }
}
