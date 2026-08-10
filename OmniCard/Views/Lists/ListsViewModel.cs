using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Lists;

public sealed partial class ListsViewModel(
    IListService listService,
    ICardService cardService,
    IDecklistService decklistService,
    IDialogService dialogService,
    IStorageContainerService containerService,
    ILogger<ListsViewModel> logger) : ObservableObject
{
    private CardGame? _game;

    public ObservableCollection<CardList> Lists { get; } = [];
    public ObservableCollection<CardListItem> Items { get; } = [];
    public ObservableCollection<CardMatch> SearchResults { get; } = [];

    [ObservableProperty]
    public partial CardList? SelectedList { get; set; }

    [ObservableProperty]
    public partial CardMatch? SelectedSearchResult { get; set; }

    [ObservableProperty]
    public partial CardListItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial string NewListName { get; set; } = "";

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial string PasteText { get; set; } = "";

    [ObservableProperty]
    public partial string ImportUrl { get; set; } = "";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IgnoreBasicLands { get; set; } = true;

    [ObservableProperty]
    public partial DecklistCheckResult? Result { get; set; }

    public Action<DecklistCheckResult>? ExportPdf { get; set; }
    public Action<DecklistCheckResult>? ExportDetailedPdf { get; set; }

    /// <summary>MTG-only: Moxfield/Archidekt URL import.</summary>
    public bool CanImportUrl => SelectedList?.Game == CardGame.Mtg;

    private static readonly HashSet<string> BasicLandNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Plains", "Island", "Swamp", "Mountain", "Forest"
    };

    public void SetGame(CardGame? game)
    {
        _game = game;
        LoadLists();
    }

    private void LoadLists()
    {
        Lists.Clear();
        Items.Clear();
        Result = null;
        SelectedList = null;
        if (_game is null) return;
        foreach (var l in listService.GetLists(_game.Value))
            Lists.Add(l);
    }

    /// <summary>Reload the lists for the current game, preserving the current selection by id.
    /// Call after an external change added/updated lists (e.g. a decklist import).</summary>
    public void Refresh()
    {
        var selectedId = SelectedList?.Id;
        LoadLists();   // clears SelectedList
        if (selectedId is int id)
            SelectedList = Lists.FirstOrDefault(l => l.Id == id);
    }

    partial void OnSelectedListChanged(CardList? value)
    {
        OnPropertyChanged(nameof(CanImportUrl));
        Result = null;
        LoadItems();
    }

    private void LoadItems()
    {
        Items.Clear();
        if (SelectedList is null) return;
        foreach (var i in listService.GetItems(SelectedList.Id))
            Items.Add(i);
    }

    [RelayCommand]
    private void CreateList()
    {
        if (_game is null) { StatusMessage = "Select a game first."; return; }
        if (string.IsNullOrWhiteSpace(NewListName)) { StatusMessage = "Enter a list name."; return; }
        var list = listService.CreateList(NewListName.Trim(), _game.Value);
        NewListName = "";
        LoadLists();
        SelectedList = Lists.FirstOrDefault(l => l.Id == list.Id);
    }

    [RelayCommand]
    private void DeleteList()
    {
        if (SelectedList is null) return;
        listService.DeleteList(SelectedList.Id);
        LoadLists();
    }

    [RelayCommand]
    private void MoveSelectedListToLocation()
    {
        if (SelectedList is null) return;
        var pick = dialogService.PickMoveListToLocation();
        if (pick is null) return;

        var container = pick.CreateNew
            ? containerService.Create(pick.NewContainerName, pick.NewContainerType)
            : pick.ExistingContainer!;

        var result = listService.CommitToLocation(SelectedList.Id, container, pick.Condition);
        LoadLists();
        StatusMessage = result.RemainingUnresolvedCount == 0
            ? $"Moved {result.AddedCount} cards to {container.Name}. List removed."
            : $"Moved {result.AddedCount} cards to {container.Name}. {result.RemainingUnresolvedCount} card(s) couldn't be resolved and remain in the list.";
    }

    [RelayCommand]
    private void Search()
    {
        SearchResults.Clear();
        if (_game is null || string.IsNullOrWhiteSpace(SearchQuery)) return;
        foreach (var m in cardService.GetGameService(_game.Value).SearchCards(SearchQuery.Trim(), 20))
            SearchResults.Add(m);
    }

    [RelayCommand]
    private void AddSelectedPrinting()
    {
        if (SelectedList is null || SelectedSearchResult is null) return;
        listService.AddPrinting(SelectedList.Id, SelectedSearchResult, isFoil: false, quantity: 1, ListItemSource.Manual);
        LoadItems();
    }

    [RelayCommand]
    private void ParsePaste()
    {
        if (SelectedList is null) { StatusMessage = "Select or create a list first."; return; }
        if (string.IsNullOrWhiteSpace(PasteText)) { StatusMessage = "Paste a decklist."; return; }
        var (_, entries) = decklistService.ParseDecklistText(PasteText);
        var result = listService.AddCardsByName(SelectedList.Id, entries);
        PasteText = "";
        LoadItems();
        StatusMessage = result.UnresolvedNames.Count == 0
            ? $"Added {result.AddedCount} cards."
            : $"Added {result.AddedCount}. Not found: {string.Join(", ", result.UnresolvedNames)}";
    }

    [RelayCommand]
    private async Task ImportUrlAsync()
    {
        if (SelectedList is null || !CanImportUrl) return;
        if (string.IsNullOrWhiteSpace(ImportUrl)) { StatusMessage = "Enter a URL."; return; }
        IsBusy = true;
        try
        {
            var fetched = await decklistService.FetchDecklistAsync(ImportUrl.Trim());
            if (fetched is null) { StatusMessage = "Couldn't reach the site. Paste the list instead."; return; }
            var (_, entries) = fetched.Value;
            var result = listService.AddCardsByName(SelectedList.Id, entries);
            ImportUrl = "";
            LoadItems();
            StatusMessage = result.UnresolvedNames.Count == 0
                ? $"Imported {result.AddedCount} cards."
                : $"Imported {result.AddedCount}. Not found: {string.Join(", ", result.UnresolvedNames)}";
        }
        catch (Exception ex)
        {
            StatusMessage = "Couldn't import from that URL.";
            logger.LogWarning(ex, "List URL import failed for {Url}", ImportUrl);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void RefreshPrices()
    {
        if (SelectedList is null) return;
        listService.RefreshPrices(SelectedList.Id);
        LoadItems();
        StatusMessage = "Prices refreshed.";
    }

    [RelayCommand]
    private void RemoveSelectedItem()
    {
        if (SelectedItem is null) return;
        listService.RemoveItem(SelectedItem.Id);
        LoadItems();
    }

    private DecklistCheckResult? BuildResult()
    {
        if (SelectedList is null) return null;
        var entries = listService.ToDecklistEntries(SelectedList.Id);
        if (IgnoreBasicLands)
            entries = entries.Where(e => !BasicLandNames.Contains(e.CardName)).ToList();
        var result = decklistService.CheckAgainstCollection(
            SelectedList.Name, "List", entries, SelectedList.Game);
        Result = result;
        StatusMessage = $"Owned: {result.TotalOwned}/{result.TotalCards} | Missing: {result.TotalMissing} | Cost: ${result.EstimatedCost:N2}";
        return result;
    }

    [RelayCommand]
    private void RunSummaryReport()
    {
        var result = BuildResult();
        if (result is not null) ExportPdf?.Invoke(result);
    }

    [RelayCommand]
    private void RunDetailedReport()
    {
        var result = BuildResult();
        if (result is not null) ExportDetailedPdf?.Invoke(result);
    }
}
