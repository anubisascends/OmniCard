using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using OmniCard.Interfaces;
using OmniCard.Models;
using System.Threading.Tasks;

namespace OmniCard.Views.Root;

public sealed partial class CollectionViewModel : ViewModel
{
    private readonly ICardService _cardService;
    private readonly IStorageContainerService _containerService;
    private readonly ICollectionPresetService _presetService;
    private readonly IDialogService _dialogService;
    private readonly ICollectionQueryService _collectionQueryService;
    private readonly IDataPathService _dataPathService;
    private readonly ILogger<CollectionViewModel> _logger;
    private readonly IEbayListingService _ebayListingService;
    private readonly EbaySettings _ebaySettings;

    /// <summary>Set by RootViewModel to delegate settings persistence.</summary>
    public Action? PersistSettings { get; set; }

    /// <summary>Set by the View for DataGrid selected cards access.</summary>
    public Func<IList<CollectionCard>>? GetSelectedCards { get; set; }

    /// <summary>Set by the View for search box focus.</summary>
    public Action? FocusSearch { get; set; }

    /// <summary>Set by RootViewModel to report status messages.</summary>
    public Action<string>? ReportMessage { get; set; }

    /// <summary>Set by RootViewModel so the home tab refreshes after collection mutations.</summary>
    public Action? CollectionChanged { get; set; }

    /// <summary>The data directory path, used by the tile art behavior to resolve scan images.</summary>
    public string DataDirectory => _dataPathService.DataDirectory;

    public CollectionViewModel(
        ICardService cardService,
        IStorageContainerService containerService,
        ICollectionPresetService presetService,
        IDialogService dialogService,
        ICollectionQueryService collectionQueryService,
        IOptions<DisplaySettings> displaySettings,
        IDataPathService dataPathService,
        ILogger<CollectionViewModel> logger,
        IEbayListingService ebayListingService,
        IOptions<EbaySettings> ebaySettings)
    {
        _cardService = cardService;
        _containerService = containerService;
        _presetService = presetService;
        _dialogService = dialogService;
        _collectionQueryService = collectionQueryService;
        _dataPathService = dataPathService;
        _logger = logger;
        _ebayListingService = ebayListingService;
        _ebaySettings = ebaySettings.Value;

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
        ["EbayStatus"] = false,
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

    // --- Manual Add ---

    [RelayCommand]
    public void OpenManualAdd()
    {
        // Use current location filter as default container, if viewing a single location
        StorageContainer? defaultContainer = null;
        if (CurrentLocationId is int id)
            defaultContainer = _containerService.GetAll().FirstOrDefault(c => c.Id == id);

        OpenManualAdd(defaultContainer);
    }

    public void OpenManualAdd(StorageContainer? defaultContainer)
    {
        var result = _dialogService.OpenManualAdd(defaultContainer);
        if (result == true)
        {
            if (ShowCardList)
            {
                // Data changed but search params are identical — force a refresh (bypass the guard).
                _ = SearchCollection();
            }
            else
                LoadOverview();
        }
    }

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
        _requestedSearch = null;
        _searchGeneration++;   // cancel any in-flight search from the view we're leaving
        MarketPrices.Clear();
        TotalCardCount = 0;
        LoadOverview();
        OnPropertyChanged(nameof(GroupedLocations));
        OnPropertyChanged(nameof(IsBulkVisible));
        OnPropertyChanged(nameof(IsOverviewSearchActive));
        OnPropertyChanged(nameof(HasVisibleLocations));
    }

    private void ResetSearchState()
    {
        CollectionSearchQuery = "";
        SelectedSortPreset = null;
        SelectedFilterPreset = null;
        _adHocSortLevels.Clear();
        IsAdHocSortActive = false;
        _matchingContainerIds = null;
    }

    // --- Overview ---

    public ObservableCollection<LocationTileSummary> LocationSummaries { get; } = [];

    [ObservableProperty]
    public partial LocationTileSummary? BulkSummary { get; set; }

    private HashSet<int>? _matchingContainerIds;

    public bool IsOverviewSearchActive => _matchingContainerIds is not null;

    public bool IsBulkVisible =>
        BulkSummary is not null &&
        (_matchingContainerIds is null || _matchingContainerIds.Contains(BulkSummary.Container.Id));

    public bool HasVisibleLocations =>
        IsBulkVisible || GroupedLocations.Any();

    public IEnumerable<IGrouping<ContainerType, LocationTileSummary>> GroupedLocations
    {
        get
        {
            var source = _matchingContainerIds is not null
                ? LocationSummaries.Where(s => _matchingContainerIds.Contains(s.Container.Id))
                : LocationSummaries;

            return source
                .Where(s => s.CardCount > 0)
                .OrderBy(s => s.Container.Name, StringComparer.OrdinalIgnoreCase)
                .GroupBy(s => s.Container.ContainerType);
        }
    }

    public void LoadOverview()
    {
        _ = LoadOverviewAsync();
    }

    private async Task LoadOverviewAsync()
    {
        LocationSummaries.Clear();
        BulkSummary = null;

        var overviews = await _collectionQueryService.GetLocationOverviewsAsync(_selectedGame);

        foreach (var summary in overviews)
        {
            if (summary.Container.IsSystem)
                BulkSummary = summary;
            else
                LocationSummaries.Add(summary);
        }

        OnPropertyChanged(nameof(GroupedLocations));
        OnPropertyChanged(nameof(IsBulkVisible));
        OnPropertyChanged(nameof(HasVisibleLocations));
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

    public void ToggleDeckCheckExclusion(int containerId)
    {
        var container = _containerService.GetAll().FirstOrDefault(c => c.Id == containerId);
        if (container is null) return;
        _containerService.SetExcludeFromDeckCheck(containerId, !container.ExcludeFromDeckCheck);
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

    /// <summary>
    /// Immutable snapshot of everything a card-list search depends on. Used to skip a
    /// redundant reload when navigation re-triggers a search with identical parameters.
    /// Presets compare by reference (plain classes): an ad-hoc sort builds a fresh
    /// <see cref="SortPreset"/> each search, so it correctly reads as "changed".
    /// </summary>
    public readonly record struct SearchParameters(
        string Query,
        CardGame Game,
        int? ContainerFilter,
        SortPreset? SortPreset,
        FilterPreset? FilterPreset,
        bool Stacked);

    [ObservableProperty]
    public partial ObservableCollection<CollectionCard> CollectionSearchResults { get; set; } = [];

    [ObservableProperty]
    public partial CollectionCard? SelectedCollectionCard { get; set; }

    [ObservableProperty]
    public partial string CollectionSearchQuery { get; set; } = "";

    /// <summary>Market price cache keyed by CollectionCard.Id.</summary>
    public Dictionary<int, decimal> MarketPrices { get; set; } = [];

    private const int PageSize = 500;

    // --- Stats ---

    [ObservableProperty]
    public partial int SelectedCardCount { get; set; }

    /// <summary>Total matching cards (including not-yet-loaded rows).</summary>
    [ObservableProperty]
    public partial int TotalCardCount { get; set; }

    public int FilteredCardCount => TotalCardCount > 0 ? TotalCardCount : CollectionSearchResults.Sum(c => c.Quantity);

    public bool HasMoreResults => CollectionSearchResults.Count < TotalCardCount;

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
        if (ShowCardList) _ = SearchCollectionCore(forceRefresh: false);
        PersistSettings?.Invoke();
    }

    private void LoadCardList()
    {
        _ = SearchCollectionCore(forceRefresh: false);
    }

    // Cached search parameters for LoadMore
    private SortPreset? _lastSortPreset;
    private int? _lastContainerFilter;
    private string _lastQuery = "";
    private CardGame? _lastGame;
    private FilterPreset? _lastFilterPreset;
    private bool _lastStacked;
    private bool _isLoadingMore;

    // Guard state for redundant/overlapping searches.
    // _requestedSearch = params of the most recently REQUESTED search (set before the
    // async load, not after it), so the guard skips a non-forced call only when an
    // identical search is already loaded or in flight — and never skips a genuinely
    // new selection just because a slower earlier search hasn't finished yet.
    // _searchGeneration increments per requested search; a slower earlier search that
    // completes after a newer one is dropped instead of clobbering the newer results.
    // Null _requestedSearch means "nothing loaded" (initial, or invalidated by NavigateBack).
    private SearchParameters? _requestedSearch;
    private int _searchGeneration;

    [RelayCommand]
    public Task SearchCollection() => SearchCollectionCore(forceRefresh: true);

    private async Task SearchCollectionCore(bool forceRefresh)
    {
        // Overview mode: filter location tiles instead of searching cards
        if (!ShowCardList)
        {
            var overviewQuery = CollectionSearchQuery;
            if (string.IsNullOrWhiteSpace(overviewQuery))
            {
                _matchingContainerIds = null;
            }
            else
            {
                _matchingContainerIds = await Task.Run(() =>
                    _cardService.GetMatchingContainerIds(overviewQuery, _selectedGame));
            }
            OnPropertyChanged(nameof(GroupedLocations));
            OnPropertyChanged(nameof(IsBulkVisible));
            OnPropertyChanged(nameof(IsOverviewSearchActive));
            OnPropertyChanged(nameof(HasVisibleLocations));
            return;
        }

        // --- existing card-list search code below (unchanged) ---
        var sortPreset = IsAdHocSortActive
            ? new SortPreset { Name = "Ad-hoc", Game = _selectedGame, SortLevels = _adHocSortLevels }
            : SelectedSortPreset;

        // Use selected container filter if set, otherwise fall back to navigation-based location
        var containerFilter = SelectedContainerFilter?.Id ?? (ShowAllCards ? null : CurrentLocationId);

        // Capture filter values for background thread and for LoadMore
        var query = CollectionSearchQuery;
        var game = _selectedGame;
        var filterPreset = SelectedFilterPreset;
        var stacked = IsStacked;

        var currentParams = new SearchParameters(query, game, containerFilter, sortPreset, filterPreset, stacked);
        if (!forceRefresh && _requestedSearch == currentParams)
        {
            _logger.LogDebug("SearchCollection skipped: parameters unchanged");
            return;
        }

        // Mark this search as the current request before awaiting, so a re-selection of
        // these same params while the load is in flight is correctly skipped, and a
        // different selection is not.
        _requestedSearch = currentParams;
        var generation = ++_searchGeneration;

        _lastSortPreset = sortPreset;
        _lastContainerFilter = containerFilter;
        _lastQuery = query;
        _lastGame = game;
        _lastFilterPreset = filterPreset;
        _lastStacked = stacked;

        // Run DB query, stacking, and pricing off the UI thread
        try
        {
            var (displayResults, prices, totalCount) = await Task.Run(() =>
            {
                // Get total count for the status bar (cheap SQL COUNT)
                var total = _cardService.GetSearchCount(query, game, containerFilter, filterPreset, stacked);

                // Load first page
                var results = new ObservableCollection<CollectionCard>();
                _cardService.SearchCollection(query, game, containerFilter, sortPreset, filterPreset, stacked, 0, PageSize, results);

                var priceCache = FetchBatchPrices(results);
                HydrateMissingImageUris(results);

                // Re-sort by MarketPrice in-memory since it's not available at DB query time
                if (sortPreset?.SortLevels.Any(l => l.Field == "MarketPrice") == true)
                {
                    var level = sortPreset.SortLevels.First(l => l.Field == "MarketPrice");
                    var sorted = level.Direction == SortDirection.Ascending
                        ? results.OrderBy(c => c.MarketPrice)
                        : results.OrderByDescending(c => c.MarketPrice);
                    results = new ObservableCollection<CollectionCard>(sorted);
                }

                return (results, priceCache, total);
            });

            // A newer search (or a NavigateBack) started while we awaited? Drop this stale result.
            if (generation != _searchGeneration)
                return;

            // Single property assignment on UI thread — DataGrid updates once
            MarketPrices = prices;
            CollectionSearchResults = displayResults;
            TotalCardCount = totalCount;
            OnPropertyChanged(nameof(FilteredCardCount));
            OnPropertyChanged(nameof(FilteredMarketValue));
            OnPropertyChanged(nameof(HasMoreResults));
        }
        catch
        {
            // Load failed: clear the request marker (unless a newer search already
            // replaced it) so the guard does not skip a retry of these same params.
            if (generation == _searchGeneration)
                _requestedSearch = null;
            throw;
        }
    }

    /// <summary>Called by the view when the user scrolls near the bottom of the DataGrid.</summary>
    public async Task LoadMore()
    {
        if (_isLoadingMore || !HasMoreResults) return;
        _isLoadingMore = true;

        var skip = CollectionSearchResults.Count;
        var sortPreset = _lastSortPreset;
        var containerFilter = _lastContainerFilter;
        var query = _lastQuery;
        var game = _lastGame;
        var filterPreset = _lastFilterPreset;
        var stacked = _lastStacked;

        var (newCards, newPrices) = await Task.Run(() =>
        {
            var batch = new ObservableCollection<CollectionCard>();
            _cardService.SearchCollection(query, game, containerFilter, sortPreset, filterPreset, stacked, skip, PageSize, batch);
            var prices = FetchBatchPrices(batch);
            HydrateMissingImageUris(batch);
            return (batch, prices);
        });

        // Append to existing collection on UI thread
        foreach (var kvp in newPrices)
            MarketPrices[kvp.Key] = kvp.Value;
        foreach (var card in newCards)
            CollectionSearchResults.Add(card);

        OnPropertyChanged(nameof(FilteredMarketValue));
        OnPropertyChanged(nameof(HasMoreResults));
        _isLoadingMore = false;
    }

    /// <summary>Re-pull prices for the currently displayed cards (no DB re-search) after a
    /// background price refresh. Prices are read off the UI thread, then applied on it so the
    /// observable MarketPrice change updates tiles in place.</summary>
    public void RefreshVisiblePrices()
    {
        if (!ShowCardList) return;
        var results = CollectionSearchResults.ToList();
        if (results.Count == 0) return;

        _ = Task.Run(() =>
        {
            var prices = new Dictionary<int, decimal>();
            foreach (var g in results.GroupBy(c => c.Game))
            {
                var gs = _cardService.GetGameService(g.Key);
                foreach (var fg in g.GroupBy(c => c.IsFoil))
                {
                    var batch = gs.GetCurrentPrices(fg.Select(c => c.GameCardId), fg.Key);
                    foreach (var c in fg)
                        prices[c.Id] = batch.GetValueOrDefault(c.GameCardId);
                }
            }
            Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var c in results)
                    if (prices.TryGetValue(c.Id, out var p)) c.MarketPrice = p;
                MarketPrices = prices;
                OnPropertyChanged(nameof(FilteredMarketValue));
            });
        });
    }

    private Dictionary<int, decimal> FetchBatchPrices(ObservableCollection<CollectionCard> results)
    {
        var priceCache = new Dictionary<int, decimal>(results.Count);
        foreach (var gameGroup in results.GroupBy(c => c.Game))
        {
            var gameService = _cardService.GetGameService(gameGroup.Key);
            foreach (var foilGroup in gameGroup.GroupBy(c => c.IsFoil))
            {
                var batchPrices = gameService.GetCurrentPrices(
                    foilGroup.Select(c => c.GameCardId), foilGroup.Key);
                foreach (var card in foilGroup)
                {
                    var price = batchPrices.GetValueOrDefault(card.GameCardId);
                    card.MarketPrice = price;
                    priceCache[card.Id] = price;
                }
            }
        }
        return priceCache;
    }

    /// <summary>
    /// Fills in <see cref="CollectionCard.ImageUri"/> for cards that don't have one stored
    /// (e.g. imported cards) by looking the print up in the game database. Runs on the search
    /// background thread so the tile art can always prefer the downloaded image. Display-only —
    /// the resolved URI is not persisted.
    /// </summary>
    private void HydrateMissingImageUris(ObservableCollection<CollectionCard> results)
    {
        foreach (var gameGroup in results.Where(c => string.IsNullOrEmpty(c.ImageUri)).GroupBy(c => c.Game))
        {
            var gameService = _cardService.GetGameService(gameGroup.Key);
            foreach (var card in gameGroup)
            {
                if (string.IsNullOrEmpty(card.GameCardId)) continue;
                try
                {
                    card.ImageUri = CardImageUriResolver.From(gameService.FindCardById(card.GameCardId));
                }
                catch
                {
                    // Leave ImageUri null; the tile falls back to scan art or a placeholder.
                }
            }
        }
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
        if (ShowCardList) _ = SearchCollectionCore(forceRefresh: false);
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
        if (ShowCardList) _ = SearchCollectionCore(forceRefresh: false);
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
        _ = SearchCollectionCore(forceRefresh: false);
    }

    [RelayCommand]
    public void ClearAdHocSort()
    {
        _adHocSortLevels.Clear();
        IsAdHocSortActive = false;
        _ = SearchCollectionCore(forceRefresh: false);
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
        if (result.HasValue) _ = SearchCollection();
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
        _ = SearchCollection();
    }

    [RelayCommand]
    public void BulkSetCollectionCondition(string condition)
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        _cardService.BulkUpdateField(ids, c => c.Condition = condition);
        ReportMessage?.Invoke($"Set condition to {condition} on {ids.Count} card(s).");
        _ = SearchCollection();
    }

    [RelayCommand]
    public void BulkSetCollectionFoil(string isFoilStr)
    {
        var isFoil = isFoilStr == "True";
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        _cardService.BulkUpdateField(ids, c => c.IsFoil = isFoil);
        ReportMessage?.Invoke($"Set {(isFoil ? "Foil" : "Non-Foil")} on {ids.Count} card(s).");
        _ = SearchCollection();
    }

    [RelayCommand]
    public void BulkDeleteCollection()
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        foreach (var id in ids)
            _cardService.DeleteCollectionCard(id);
        ReportMessage?.Invoke($"Deleted {ids.Count} card(s).");
        // Data changed but search params are identical — force a refresh (bypass the guard).
        _ = SearchCollection();
        CollectionChanged?.Invoke();
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
            _ = SearchCollection();
        }
    }

    // --- Containers ---

    public ObservableCollection<StorageContainer> AvailableContainers { get; } = [];

    [ObservableProperty]
    public partial StorageContainer? SelectedContainerFilter { get; set; }

    partial void OnSelectedContainerFilterChanged(StorageContainer? value)
    {
        if (ShowCardList) _ = SearchCollectionCore(forceRefresh: false);
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

    // --- eBay commands ---

    [RelayCommand]
    public void ListOnEbay()
    {
        var selected = GetSelectedCards?.Invoke();
        if (selected is null || selected.Count != 1) return;
        var card = selected[0];
        if (card.EbayListing?.Status == EbayListingStatus.Active) return;

        var result = _dialogService.OpenEbayListingDialog(card);
        if (result == true)
        {
            ReportMessage?.Invoke($"Listed \"{card.Name}\" on eBay.");
            _ = SearchCollection();
        }
    }

    [RelayCommand]
    public void ViewOnEbay()
    {
        var selected = GetSelectedCards?.Invoke();
        if (selected is null || selected.Count != 1) return;
        var listing = selected[0].EbayListing;
        if (listing is null || string.IsNullOrEmpty(listing.EbayItemId)) return;

        var viewBaseUrl = _ebaySettings.Environment == "production"
            ? "https://www.ebay.com"
            : "https://www.sandbox.ebay.com";

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = $"{viewBaseUrl}/itm/{listing.EbayItemId}",
            UseShellExecute = true,
        });
    }

    [RelayCommand]
    public async Task EndEbayListing()
    {
        var selected = GetSelectedCards?.Invoke();
        if (selected is null || selected.Count != 1) return;
        var listing = selected[0].EbayListing;
        if (listing is null || listing.Status != EbayListingStatus.Active) return;

        var result = System.Windows.MessageBox.Show(
            $"End the eBay listing for \"{selected[0].Name}\"?",
            "End Listing",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        var success = await _ebayListingService.EndListingAsync(listing);
        if (success)
        {
            ReportMessage?.Invoke($"Ended eBay listing for \"{selected[0].Name}\".");
            _ = SearchCollection();
        }
        else
        {
            ReportMessage?.Invoke("Failed to end eBay listing.");
        }
    }

    public void Initialize()
    {
        LoadContainers();
        LoadPresets();
        LoadOverview();
    }
}
