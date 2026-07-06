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

    /// <summary>Set by RootViewModel so the home tab refreshes after collection mutations.</summary>
    public Action? CollectionChanged { get; set; }

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
        TotalCardCount = 0;
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
        LocationSummaries
            .OrderBy(s => s.Container.Name, StringComparer.OrdinalIgnoreCase)
            .GroupBy(s => s.Container.ContainerType);

    public void LoadOverview()
    {
        LocationSummaries.Clear();
        BulkSummary = null;

        var containers = _containerService.GetAll();
        using var context = _dbContextFactory.CreateDbContext();
        var cardsQuery = context.Cards.AsNoTracking();

        // SQL aggregate: count + purchase total per container
        var aggregates = cardsQuery
            .GroupBy(c => c.ContainerId)
            .Select(g => new
            {
                ContainerId = g.Key,
                Count = g.Count(),
                PurchaseTotal = g.Sum(c => c.PurchasePrice ?? 0m)
            })
            .ToDictionary(a => a.ContainerId);

        // Lightweight projection for price data (no full card materialization)
        var priceData = cardsQuery
            .Select(c => new { c.ContainerId, c.GameCardId, c.IsFoil, c.Game })
            .ToList();

        // Batch price lookup grouped by (Game, IsFoil)
        var allPrices = new Dictionary<(string GameCardId, bool IsFoil), decimal>();
        foreach (var gameGroup in priceData.GroupBy(c => c.Game))
        {
            var gameService = _cardService.GetGameService(gameGroup.Key);
            foreach (var foilGroup in gameGroup.GroupBy(c => c.IsFoil))
            {
                var batchPrices = gameService.GetCurrentPrices(
                    foilGroup.Select(c => c.GameCardId).Distinct(), foilGroup.Key);
                foreach (var kvp in batchPrices)
                    allPrices.TryAdd((kvp.Key, foilGroup.Key), kvp.Value);
            }
        }

        // Market totals per container
        var marketTotals = priceData
            .GroupBy(c => c.ContainerId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(c => allPrices.GetValueOrDefault((c.GameCardId, c.IsFoil))));

        // Set symbols per container (SQL distinct)
        var setsByContainer = cardsQuery
            .Select(c => new { c.ContainerId, c.SetCode })
            .Distinct()
            .ToList()
            .GroupBy(c => c.ContainerId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(c => c.SetCode)
                      .Select(c => new SetCodeRarity { SetCode = c.SetCode, Rarity = "uncommon" })
                      .ToList());

        // Cover images: only fetch the specific cards needed
        var coverCardIds = containers
            .Where(c => c.CoverCardId.HasValue)
            .Select(c => c.CoverCardId!.Value)
            .ToList();
        var coverImages = coverCardIds.Count > 0
            ? cardsQuery
                .Where(c => coverCardIds.Contains(c.Id))
                .Select(c => new { c.Id, c.ImageUri })
                .ToDictionary(c => c.Id, c => c.ImageUri)
            : [];
        // Fallback cover images: first card per container
        var fallbackCovers = cardsQuery
            .GroupBy(c => c.ContainerId)
            .Select(g => new { ContainerId = g.Key, ImageUri = g.Select(c => c.ImageUri).FirstOrDefault() })
            .ToDictionary(c => c.ContainerId, c => c.ImageUri);

        foreach (var container in containers)
        {
            var agg = aggregates.GetValueOrDefault(container.Id);
            var cardCount = agg?.Count ?? 0;
            var totalPurchase = agg?.PurchaseTotal ?? 0m;
            var totalMarket = marketTotals.GetValueOrDefault(container.Id);

            var delta = totalMarket - totalPurchase;
            var deltaPercent = totalPurchase > 0 ? (double)(delta / totalPurchase) * 100 : 0;

            // Resolve cover image
            string? coverUri = null;
            if (container.CoverCardId.HasValue)
                coverImages.TryGetValue(container.CoverCardId.Value, out coverUri);
            coverUri ??= fallbackCovers.GetValueOrDefault(container.Id);

            var summary = new LocationTileSummary
            {
                Container = container,
                CardCount = cardCount,
                TotalMarketValue = totalMarket,
                TotalPurchaseCost = totalPurchase,
                PriceDelta = delta,
                PriceDeltaPercent = deltaPercent,
                CoverImageUri = coverUri,
                SetSymbols = setsByContainer.GetValueOrDefault(container.Id, []),
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
        if (ShowCardList) _ = SearchCollection();
        PersistSettings?.Invoke();
    }

    private void LoadCardList()
    {
        _ = SearchCollection();
    }

    // Cached search parameters for LoadMore
    private SortPreset? _lastSortPreset;
    private int? _lastContainerFilter;
    private string _lastQuery = "";
    private CardGame? _lastGame;
    private FilterPreset? _lastFilterPreset;
    private bool _lastStacked;
    private bool _isLoadingMore;

    [RelayCommand]
    public async Task SearchCollection()
    {
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
        _lastSortPreset = sortPreset;
        _lastContainerFilter = containerFilter;
        _lastQuery = query;
        _lastGame = game;
        _lastFilterPreset = filterPreset;
        _lastStacked = stacked;

        // Run DB query, stacking, and pricing off the UI thread
        var (displayResults, prices, totalCount) = await Task.Run(() =>
        {
            // Get total count for the status bar (cheap SQL COUNT)
            var total = _cardService.GetSearchCount(query, game, containerFilter, filterPreset, stacked);

            // Load first page
            var results = new ObservableCollection<CollectionCard>();
            _cardService.SearchCollection(query, game, containerFilter, sortPreset, filterPreset, stacked, 0, PageSize, results);

            var priceCache = FetchBatchPrices(results);

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

        // Single property assignment on UI thread — DataGrid updates once
        MarketPrices = prices;
        CollectionSearchResults = displayResults;
        TotalCardCount = totalCount;
        OnPropertyChanged(nameof(FilteredCardCount));
        OnPropertyChanged(nameof(FilteredMarketValue));
        OnPropertyChanged(nameof(HasMoreResults));
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
        if (ShowCardList) _ = SearchCollection();
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
        if (ShowCardList) _ = SearchCollection();
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
        _ = SearchCollection();
    }

    [RelayCommand]
    public void ClearAdHocSort()
    {
        _adHocSortLevels.Clear();
        IsAdHocSortActive = false;
        _ = SearchCollection();
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
        if (ShowCardList) _ = SearchCollection();
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
