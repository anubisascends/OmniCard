using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;
using System.Linq;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Views.Root;

public sealed partial class CollectionViewModel : ViewModel
{
    private readonly ICardService _cardService;
    private readonly IStorageContainerService _containerService;
    private readonly ICollectionPresetService _presetService;
    private readonly IDialogService _dialogService;
    private readonly IDbContextFactory<CollectionDbContext> _dbContextFactory;
    private readonly IDataPathService _dataPathService;
    private readonly ILogger<CollectionViewModel> _logger;

    /// <summary>Set by RootViewModel to delegate settings persistence.</summary>
    public Action? PersistSettings { get; set; }

    /// <summary>Set by the View for DataGrid selected cards access.</summary>
    public Func<IList<CollectionCard>>? GetSelectedCards { get; set; }

    /// <summary>Set by the View for search box focus.</summary>
    public Action? FocusSearch { get; set; }

    /// <summary>Set by RootViewModel to report status messages.</summary>
    public Action<string>? ReportMessage { get; set; }

    /// <summary>The data directory path, used as converter parameter for CardPreviewImageConverter.</summary>
    public string DataDirectory => _dataPathService.DataDirectory;

    public CollectionViewModel(
        ICardService cardService,
        IStorageContainerService containerService,
        ICollectionPresetService presetService,
        IDialogService dialogService,
        IDbContextFactory<CollectionDbContext> dbContextFactory,
        IOptions<DisplaySettings> displaySettings,
        IDataPathService dataPathService,
        ILogger<CollectionViewModel> logger)
    {
        _cardService = cardService;
        _containerService = containerService;
        _presetService = presetService;
        _dialogService = dialogService;
        _dbContextFactory = dbContextFactory;
        _dataPathService = dataPathService;
        _logger = logger;

        // Initialize column visibility from settings
        var saved = displaySettings.Value.CollectionColumnVisibility;
        foreach (var col in AllColumns)
            _columnVisibility[col.Key] = saved.TryGetValue(col.Key, out var v) ? v : col.Value;

        IsStacked = displaySettings.Value.StackDuplicates;
    }

    // --- Column definitions: Key = column name, Value = default visibility ---
    private static readonly Dictionary<string, bool> AllColumns = new()
    {
        ["Name"] = true,
        ["Set"] = true,
        ["Number"] = true,
        ["Type"] = true,
        ["Rarity"] = true,
        ["Finish"] = true,
        ["MarketPrice"] = true,
        ["Game"] = false,
        ["Color"] = false,
        ["Condition"] = false,
        ["PurchasePrice"] = false,
        ["DateAdded"] = false,
        ["Location"] = false,
        ["SetCode"] = false,
    };

    private readonly Dictionary<string, bool> _columnVisibility = new();

    public IReadOnlyDictionary<string, bool> ColumnVisibility => _columnVisibility;

    public bool IsColumnVisible(string column) =>
        _columnVisibility.TryGetValue(column, out var v) && v;

    [RelayCommand]
    public void ToggleColumnVisibility(string column)
    {
        if (!_columnVisibility.ContainsKey(column)) return;
        _columnVisibility[column] = !_columnVisibility[column];
        OnPropertyChanged(nameof(ColumnVisibility));
        PersistSettings?.Invoke();
    }

    public Dictionary<string, bool> GetColumnVisibilityForPersistence() => new(_columnVisibility);

    // --- Navigation ---

    [ObservableProperty]
    public partial bool ShowCardList { get; set; }

    [ObservableProperty]
    public partial int? CurrentLocationId { get; set; }

    [ObservableProperty]
    public partial bool ShowAllCards { get; set; }

    [ObservableProperty]
    public partial string CurrentLocationName { get; set; } = "";

    [RelayCommand]
    public void NavigateToLocation(int locationId)
    {
        var container = _containerService.GetAll().FirstOrDefault(c => c.Id == locationId);
        CurrentLocationId = locationId;
        CurrentLocationName = container?.Name ?? "";
        ShowAllCards = false;

        ResetSearchState();
        ShowCardList = true;

        // Force Location column hidden in single-location view
        _columnVisibility["Location"] = false;
        OnPropertyChanged(nameof(ColumnVisibility));

        LoadCardList();
    }

    [RelayCommand]
    public void BrowseAll()
    {
        CurrentLocationId = null;
        CurrentLocationName = "Entire Collection";
        ShowAllCards = true;

        ResetSearchState();
        ShowCardList = true;

        // Auto-show Location column
        _columnVisibility["Location"] = true;
        OnPropertyChanged(nameof(ColumnVisibility));

        LoadCardList();
    }

    [RelayCommand]
    public void NavigateBack()
    {
        ShowCardList = false;
        ShowAllCards = false;
        CurrentLocationId = null;
        CurrentLocationName = "";
        ResetSearchState();
        CollectionSearchResults.Clear();
        MarketPrices.Clear();
        LoadOverview();
    }

    private void ResetSearchState()
    {
        CollectionSearchQuery = "";
        SelectedSortPreset = null;
        SelectedFilterPreset = null;
        _adHocSortLevels.Clear();
        IsAdHocSortActive = false;
    }

    // --- Overview ---

    public ObservableCollection<LocationTileSummary> LocationSummaries { get; } = [];

    [ObservableProperty]
    public partial LocationTileSummary? BulkSummary { get; set; }

    public IEnumerable<IGrouping<ContainerType, LocationTileSummary>> GroupedLocations =>
        LocationSummaries.GroupBy(s => s.Container.ContainerType);

    public void LoadOverview()
    {
        LocationSummaries.Clear();
        BulkSummary = null;

        var containers = _containerService.GetAll();
        using var context = _dbContextFactory.CreateDbContext();

        foreach (var container in containers)
        {
            var cards = context.Cards
                .AsNoTracking()
                .Where(c => c.ContainerId == container.Id)
                .ToList();

            decimal totalMarket = 0;
            decimal totalPurchase = 0;
            foreach (var card in cards)
            {
                var market = _cardService.GetGameService(card.Game).GetCurrentPrice(card.GameCardId, card.IsFoil) ?? 0;
                totalMarket += market;
                if (card.PurchasePrice.HasValue)
                    totalPurchase += card.PurchasePrice.Value;
            }

            var delta = totalMarket - totalPurchase;
            var deltaPercent = totalPurchase > 0 ? (double)(delta / totalPurchase) * 100 : 0;

            // Resolve cover image
            string? coverUri = null;
            if (container.CoverCardId.HasValue)
            {
                var coverCard = cards.FirstOrDefault(c => c.Id == container.CoverCardId.Value);
                coverUri = coverCard?.ImageUri;
            }
            coverUri ??= cards.FirstOrDefault()?.ImageUri;

            // Collect distinct sets for symbol display (uncommon/silver for visibility on dark tiles)
            var setSymbols = cards
                .Select(c => c.SetCode)
                .Distinct()
                .OrderBy(s => s)
                .Select(s => new SetCodeRarity { SetCode = s, Rarity = "uncommon" })
                .ToList();

            var summary = new LocationTileSummary
            {
                Container = container,
                CardCount = cards.Count,
                TotalMarketValue = totalMarket,
                TotalPurchaseCost = totalPurchase,
                PriceDelta = delta,
                PriceDeltaPercent = deltaPercent,
                CoverImageUri = coverUri,
                SetSymbols = setSymbols,
            };

            if (container.IsSystem)
                BulkSummary = summary;
            else
                LocationSummaries.Add(summary);
        }

        OnPropertyChanged(nameof(GroupedLocations));
    }

    public void DeleteLocationWithOptions(int containerId, bool moveCardsToBulk)
    {
        _containerService.Delete(containerId, moveCardsToBulk);
        _logger.LogInformation("Deleted location {Id} (moveCardsToBulk={Move})", containerId, moveCardsToBulk);
        LoadContainers();
        LoadOverview();
    }

    public void SetCoverCard(int containerId, int? cardId)
    {
        _containerService.SetCoverCard(containerId, cardId);
        LoadOverview();
    }

    public void SetCoverArt(int containerId)
    {
        var container = _containerService.GetAll().FirstOrDefault(c => c.Id == containerId);
        var cardId = _dialogService.PickCoverArt(containerId, container?.Name ?? "");
        if (cardId.HasValue)
        {
            _containerService.SetCoverCard(containerId, cardId.Value);
            LoadOverview();
        }
    }

    // --- Card List ---

    [ObservableProperty]
    public partial ObservableCollection<CollectionCard> CollectionSearchResults { get; set; } = [];

    [ObservableProperty]
    public partial CollectionCard? SelectedCollectionCard { get; set; }

    [ObservableProperty]
    public partial string CollectionSearchQuery { get; set; } = "";

    /// <summary>Market price cache keyed by CollectionCard.Id.</summary>
    public Dictionary<int, decimal> MarketPrices { get; } = [];

    // --- Stats ---

    [ObservableProperty]
    public partial int SelectedCardCount { get; set; }

    public int FilteredCardCount => CollectionSearchResults.Sum(c => c.Quantity);

    public decimal FilteredMarketValue
    {
        get
        {
            decimal total = 0;
            foreach (var card in CollectionSearchResults)
            {
                if (MarketPrices.TryGetValue(card.Id, out var price))
                    total += price * card.Quantity;
            }
            return total;
        }
    }

    // --- Stacking ---

    [ObservableProperty]
    public partial bool IsStacked { get; set; }

    partial void OnIsStackedChanged(bool value)
    {
        if (ShowCardList) SearchCollection();
        PersistSettings?.Invoke();
    }

    private void LoadCardList()
    {
        SearchCollection();
    }

    [RelayCommand]
    public void SearchCollection()
    {
        var sortPreset = IsAdHocSortActive
            ? new SortPreset { Name = "Ad-hoc", Game = _selectedGame, SortLevels = _adHocSortLevels }
            : SelectedSortPreset;

        // Use selected container filter if set, otherwise fall back to navigation-based location
        var containerFilter = SelectedContainerFilter?.Id ?? (ShowAllCards ? null : CurrentLocationId);

        // Build results in a disconnected collection (no UI listeners)
        var rawResults = new ObservableCollection<CollectionCard>();
        _cardService.SearchCollection(
            CollectionSearchQuery,
            _selectedGame,
            containerFilter,
            sortPreset,
            SelectedFilterPreset,
            rawResults);

        // Stack identical cards if enabled
        ObservableCollection<CollectionCard> displayResults;
        if (IsStacked)
        {
            displayResults = new ObservableCollection<CollectionCard>();
            foreach (var group in rawResults.GroupBy(c => (c.GameCardId, c.IsFoil, c.Condition)))
            {
                var items = group.ToList();
                var rep = items[0];
                rep.Quantity = items.Count;
                rep.StackedIds = items.Select(c => c.Id).ToList();
                displayResults.Add(rep);
            }
        }
        else
        {
            displayResults = rawResults;
        }

        // Cache market prices before binding to UI
        MarketPrices.Clear();
        foreach (var card in displayResults)
        {
            var price = _cardService.GetGameService(card.Game).GetCurrentPrice(card.GameCardId, card.IsFoil) ?? 0;
            MarketPrices[card.Id] = price;
        }

        // Single property assignment — DataGrid updates once
        CollectionSearchResults = displayResults;
        OnPropertyChanged(nameof(MarketPrices));
        OnPropertyChanged(nameof(FilteredCardCount));
        OnPropertyChanged(nameof(FilteredMarketValue));
    }

    // --- Sort/Filter ---

    public ObservableCollection<SortPreset> AvailableSortPresets { get; } = [];
    public ObservableCollection<FilterPreset> AvailableFilterPresets { get; } = [];

    [ObservableProperty]
    public partial SortPreset? SelectedSortPreset { get; set; }

    partial void OnSelectedSortPresetChanged(SortPreset? value)
    {
        IsAdHocSortActive = false;
        _adHocSortLevels.Clear();
        if (value is not null)
            _presetService.SetActiveSortPreset(_selectedGame, value.Name);
        else
            _presetService.SetActiveSortPreset(_selectedGame, null);
        if (ShowCardList) SearchCollection();
    }

    [RelayCommand]
    public void ResetSelectedSortPreset() => SelectedSortPreset = null;

    [ObservableProperty]
    public partial FilterPreset? SelectedFilterPreset { get; set; }

    partial void OnSelectedFilterPresetChanged(FilterPreset? value)
    {
        if (value is not null)
            _presetService.SetActiveFilterPreset(_selectedGame, value.Name);
        else
            _presetService.SetActiveFilterPreset(_selectedGame, null);
        if (ShowCardList) SearchCollection();
    }

    [RelayCommand]
    public void ResetSelectedFilterPreset() => SelectedFilterPreset = null;

    [ObservableProperty]
    public partial bool IsAdHocSortActive { get; set; }

    private List<SortLevel> _adHocSortLevels = [];

    [RelayCommand]
    public void ApplyColumnSort(string field)
    {
        var isAppend = field.StartsWith('+');
        if (isAppend) field = field[1..];

        var existing = _adHocSortLevels.FirstOrDefault(l => l.Field == field);
        if (existing is not null)
        {
            if (existing.Direction == SortDirection.Ascending)
                existing.Direction = SortDirection.Descending;
            else
                _adHocSortLevels.Remove(existing);
        }
        else
        {
            if (!isAppend) _adHocSortLevels.Clear();
            _adHocSortLevels.Add(new SortLevel { Field = field, Direction = SortDirection.Ascending });
        }

        IsAdHocSortActive = _adHocSortLevels.Count > 0;
        SearchCollection();
    }

    [RelayCommand]
    public void ClearAdHocSort()
    {
        _adHocSortLevels.Clear();
        IsAdHocSortActive = false;
        SearchCollection();
    }

    // --- Collection actions ---

    /// <summary>Resolve all real card IDs from selected rows (expands stacked entries).</summary>
    private List<int> GetAllSelectedCardIds()
    {
        var selected = GetSelectedCards?.Invoke();
        if (selected is null or { Count: 0 }) return [];
        return selected.SelectMany(c => c.StackedIds ?? [c.Id]).ToList();
    }

    [RelayCommand]
    public void CollectionCardDoubleClick()
    {
        if (SelectedCollectionCard is null) return;
        _logger.LogInformation("Editing collection card {Id}: {Name}", SelectedCollectionCard.Id, SelectedCollectionCard.Name);
        var result = _dialogService.EditCollectionCard(SelectedCollectionCard);
        if (result.HasValue) SearchCollection();
    }

    [RelayCommand]
    public void MoveSelectedToLocation()
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;

        var result = _dialogService.PickMoveToLocation();
        if (result is null) return;

        _cardService.MoveCardsToContainer(ids, result.Container.Id, result.Section);
        ReportMessage?.Invoke($"Moved {ids.Count} card(s) to {result.Container.Name}.");
        SearchCollection();
    }

    [RelayCommand]
    public void BulkSetCollectionCondition(string condition)
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        _cardService.BulkUpdateField(ids, c => c.Condition = condition);
        ReportMessage?.Invoke($"Set condition to {condition} on {ids.Count} card(s).");
        SearchCollection();
    }

    [RelayCommand]
    public void BulkSetCollectionFoil(string isFoilStr)
    {
        var isFoil = isFoilStr == "True";
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        _cardService.BulkUpdateField(ids, c => c.IsFoil = isFoil);
        ReportMessage?.Invoke($"Set {(isFoil ? "Foil" : "Non-Foil")} on {ids.Count} card(s).");
        SearchCollection();
    }

    [RelayCommand]
    public void BulkDeleteCollection()
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        foreach (var id in ids)
            _cardService.DeleteCollectionCard(id);
        ReportMessage?.Invoke($"Deleted {ids.Count} card(s).");
        SearchCollection();
    }

    [RelayCommand]
    public void CopyCollectionCardNames()
    {
        var selected = GetSelectedCards?.Invoke();
        if (selected is null or { Count: 0 }) return;
        var names = string.Join(Environment.NewLine, selected.Select(c => c.Name));
        System.Windows.Clipboard.SetText(names);
    }

    [RelayCommand]
    public void OpenSortFilterBuilder()
    {
        var changed = _dialogService.OpenSortFilterBuilder(_selectedGame);
        if (changed)
        {
            LoadPresets();
            SearchCollection();
        }
    }

    // --- Containers ---

    public ObservableCollection<StorageContainer> AvailableContainers { get; } = [];

    [ObservableProperty]
    public partial StorageContainer? SelectedContainerFilter { get; set; }

    partial void OnSelectedContainerFilterChanged(StorageContainer? value)
    {
        if (ShowCardList) SearchCollection();
    }

    public void LoadContainers()
    {
        AvailableContainers.Clear();
        foreach (var c in _containerService.GetAll())
            AvailableContainers.Add(c);
    }

    // --- Game context (set by RootViewModel when game changes) ---

    private CardGame _selectedGame;

    public void SetGame(CardGame game)
    {
        _selectedGame = game;
        LoadPresets();
    }

    public void LoadPresets()
    {
        AvailableSortPresets.Clear();
        foreach (var p in _presetService.GetSortPresets(_selectedGame))
            AvailableSortPresets.Add(p);

        AvailableFilterPresets.Clear();
        foreach (var p in _presetService.GetFilterPresets(_selectedGame))
            AvailableFilterPresets.Add(p);

        SelectedSortPreset = _presetService.GetActiveSortPreset(_selectedGame);
        SelectedFilterPreset = _presetService.GetActiveFilterPreset(_selectedGame);
    }

    public void Initialize()
    {
        LoadContainers();
        LoadPresets();
        LoadOverview();
    }
}
