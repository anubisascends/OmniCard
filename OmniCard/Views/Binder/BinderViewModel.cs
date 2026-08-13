using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Binder;

/// <summary>One rendered slot in a binder page grid: its page/slot coordinates (for drag-and-drop
/// targeting) plus whatever card currently occupies it (null = empty slot).</summary>
public sealed record BinderSlotItem(int Page, int SlotIndex, CollectionCard? Card);

/// <summary>One clickable entry in the pagination strip beneath the binder — a single spread
/// (page 1 alone, or a left/right page pair). <see cref="IsCurrent"/> is toggled as the user
/// navigates so the strip can highlight where they are.</summary>
public sealed partial class BinderSpreadTab(int index, string label) : ObservableObject
{
    public int Index { get; } = index;
    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}

public sealed partial class BinderViewModel(
    IStorageContainerService containerService,
    ICardService cardService,
    IDialogService dialogService,
    IListingService listingService,
    ITagService tagService,
    IEbayListingService ebayListingService,
    IOptions<EbaySettings> ebaySettings,
    IDataPathService dataPathService) : ViewModel
{
    private readonly EbaySettings _ebaySettings = ebaySettings.Value;

    public string DataDirectory => dataPathService.DataDirectory;
    public bool IsStacked => false;

    public ObservableCollection<CollectionCard> UnplacedCards { get; } = [];

    /// <summary>The left-hand page of the current spread. Empty for spread 0 — page 1 opens alone
    /// on the right, like a binder's first page facing the (blank) inside front cover.</summary>
    public ObservableCollection<BinderSlotItem> LeftPageSlots { get; } = [];

    /// <summary>The right-hand page of the spread. Empty only when the binder has an even total
    /// page count and this is the trailing spread (the last page then falls on the left, alone,
    /// facing the blank inside back cover).</summary>
    public ObservableCollection<BinderSlotItem> RightPageSlots { get; } = [];

    [ObservableProperty]
    public partial string ContainerName { get; set; } = "";

    /// <summary>0-based index of the current spread. Spread 0 is page 1 alone (right side); spread
    /// N&gt;=1 shows pages (2N, 2N+1) — mirroring how a real binder opens: page 1 stands alone
    /// against the inside front cover, then every following turn reveals a left/right pair.</summary>
    [ObservableProperty]
    public partial int SpreadIndex { get; set; }

    [ObservableProperty]
    public partial int TotalPages { get; set; } = 1;

    [ObservableProperty]
    public partial int SlotsPerPage { get; set; } = 9;

    [ObservableProperty]
    public partial int Columns { get; set; } = 3;

    /// <summary>Editable copies bound to the layout textboxes in the header; only take effect
    /// when ApplyLayout runs, so a card mid-drag doesn't get its grid reshuffled underneath it
    /// on every keystroke.</summary>
    [ObservableProperty]
    public partial int PendingSlotsPerPage { get; set; } = 9;

    [ObservableProperty]
    public partial int PendingColumns { get; set; } = 3;

    public int? LeftPageNumber => SpreadIndex == 0 ? null : SpreadIndex * 2;
    public int? RightPageNumber => SpreadIndex == 0 ? 1 : (SpreadIndex * 2 + 1 <= TotalPages ? SpreadIndex * 2 + 1 : null);
    public bool HasLeftPage => LeftPageNumber is not null;
    public bool HasRightPage => RightPageNumber is not null;

    public string PageRangeLabel => HasLeftPage
        ? (HasRightPage ? $"Pages {LeftPageNumber}-{RightPageNumber}" : $"Page {LeftPageNumber}")
        : $"Page {RightPageNumber}";

    // Page 1 is its own spread (0); pages 2..TotalPages pair up two-per-spread thereafter.
    private int TotalSpreads => 1 + TotalPages / 2;
    public bool CanGoToPreviousSpread => SpreadIndex > 0;
    public bool CanGoToNextSpread => SpreadIndex < TotalSpreads - 1;

    /// <summary>Clickable pagination strip beneath the binder — one entry per spread, in reading
    /// order. Rebuilt whenever the page count changes; the current entry is flagged via
    /// <see cref="BinderSpreadTab.IsCurrent"/> so the strip can highlight it.</summary>
    public ObservableCollection<BinderSpreadTab> SpreadTabs { get; } = [];

    private void RebuildSpreadTabs()
    {
        SpreadTabs.Clear();
        for (var i = 0; i < TotalSpreads; i++)
        {
            string label;
            if (i == 0)
            {
                label = "1";
            }
            else
            {
                var left = i * 2;
                var right = i * 2 + 1;
                label = right <= TotalPages ? $"{left}–{right}" : $"{left}";
            }

            SpreadTabs.Add(new BinderSpreadTab(i, label));
        }

        UpdateCurrentSpreadTab();
    }

    private void UpdateCurrentSpreadTab()
    {
        foreach (var tab in SpreadTabs)
            tab.IsCurrent = tab.Index == SpreadIndex;
    }

    partial void OnTotalPagesChanged(int value) => RebuildSpreadTabs();

    private const int SlotWidth = 172;
    private const int SlotHeight = 240;

    /// <summary>Fixed footprint of one binder page (Columns x rows-to-fit-SlotsPerPage), in
    /// pixels. Both the left and right page containers are always sized to this — even the side
    /// with no facing page (page 1 alone, or a trailing lone page) — so the binder's overall open
    /// shape stays constant across every spread, matching a real binder rather than visibly
    /// shrinking to a single page's width when there's nothing to show on one side.</summary>
    public int PageWidth => Columns * SlotWidth;
    public int PageHeight => (int)Math.Ceiling((double)SlotsPerPage / Math.Max(Columns, 1)) * SlotHeight;

    partial void OnColumnsChanged(int value)
    {
        OnPropertyChanged(nameof(PageWidth));
        OnPropertyChanged(nameof(PageHeight));
    }

    partial void OnSlotsPerPageChanged(int value) => OnPropertyChanged(nameof(PageHeight));

    private int _containerId;
    /// <summary>Loads a binder into the dialog. Always starts on the first spread.</summary>
    public void Load(int containerId)
    {
        _containerId = containerId;
        var container = containerService.GetAll().FirstOrDefault(c => c.Id == containerId);
        ContainerName = container?.Name ?? "";

        var layout = containerService.GetBinderLayout(containerId);
        SlotsPerPage = layout.SlotsPerPage;
        TotalPages = layout.TotalPages;
        Columns = layout.Columns;
        PendingSlotsPerPage = layout.SlotsPerPage;
        PendingColumns = layout.Columns;
        SpreadIndex = 0;

        RebuildSpreadTabs(); // explicit — OnTotalPagesChanged only fires when the value actually differs
        Refresh();
    }

    [RelayCommand]
    public void ApplyLayout()
    {
        if (PendingSlotsPerPage <= 0 || PendingColumns <= 0) return;

        containerService.SetSlotsPerPage(_containerId, PendingSlotsPerPage);
        containerService.SetColumns(_containerId, PendingColumns);
        SlotsPerPage = PendingSlotsPerPage;
        Columns = PendingColumns;
        Refresh();
    }

    /// <summary>Live filter over the Unplaced Cards pool, using the same Scryfall query syntax as
    /// the main collection search (e.g. <c>c:u</c>, <c>r&gt;=rare</c>, <c>t:creature foil:true</c>).
    /// Runs server-side via <see cref="ICardService.GetUnplacedBinderCards"/> so it behaves
    /// identically to the toolbar search rather than a plain substring match.</summary>
    [ObservableProperty]
    public partial string UnplacedFilterQuery { get; set; } = "";

    /// <summary>Applies the Unplaced filter — bound to Enter in the filter box (like the main
    /// collection search), so the pool is only re-queried on submit, not on every keystroke.</summary>
    [RelayCommand]
    public void ApplyUnplacedFilter() => RefreshUnplaced();

    /// <summary>Re-queries just the Unplaced pool (narrowed by <see cref="UnplacedFilterQuery"/>)
    /// and refreshes its tags — without touching the pages.</summary>
    private void RefreshUnplaced()
    {
        PopulateUnplaced();
        AttachTagsTo(UnplacedCards);
    }

    private void PopulateUnplaced()
    {
        var preset = string.IsNullOrWhiteSpace(UnplacedFilterQuery)
            ? null
            : new FilterPreset { Query = UnplacedFilterQuery };

        var unplaced = cardService.GetUnplacedBinderCards(_containerId, preset);
        HydrateMissingImageUris(unplaced);

        UnplacedCards.Clear();
        foreach (var c in unplaced)
            UnplacedCards.Add(c);
    }

    private void Refresh()
    {
        PopulateUnplaced();

        if (SpreadIndex >= TotalSpreads) SpreadIndex = Math.Max(0, TotalSpreads - 1);
        if (SpreadIndex < 0) SpreadIndex = 0;

        OnPropertyChanged(nameof(LeftPageNumber));
        OnPropertyChanged(nameof(RightPageNumber));
        OnPropertyChanged(nameof(HasLeftPage));
        OnPropertyChanged(nameof(HasRightPage));
        OnPropertyChanged(nameof(PageRangeLabel));

        LeftPageSlots.Clear();
        if (LeftPageNumber is int leftPage)
            FillPage(LeftPageSlots, leftPage);

        RightPageSlots.Clear();
        if (RightPageNumber is int rightPage)
            FillPage(RightPageSlots, rightPage);

        OnPropertyChanged(nameof(CanGoToPreviousSpread));
        OnPropertyChanged(nameof(CanGoToNextSpread));
        FirstSpreadCommand.NotifyCanExecuteChanged();
        PreviousSpreadCommand.NotifyCanExecuteChanged();
        NextSpreadCommand.NotifyCanExecuteChanged();
        LastSpreadCommand.NotifyCanExecuteChanged();
        UpdateCurrentSpreadTab();

        AttachTags();
    }

    /// <summary>Populates each loaded card's Tags (not part of the base query — see
    /// CollectionCard.Tags doc comment) so tile badges and the Tags flyout stay accurate.</summary>
    private void AttachTags() => AttachTagsTo(UnplacedCards
        .Concat(LeftPageSlots.Select(s => s.Card))
        .Concat(RightPageSlots.Select(s => s.Card))
        .Where(c => c is not null)
        .Select(c => c!));

    private void AttachTagsTo(IEnumerable<CollectionCard> cards)
    {
        var list = cards.ToList();
        if (list.Count == 0) return;

        var tagsByLot = tagService.GetTagsByLots(list.Select(c => c.Id));
        foreach (var card in list)
            card.Tags = tagsByLot.TryGetValue(card.Id, out var tags) ? tags : [];
    }

    private void FillPage(ObservableCollection<BinderSlotItem> slots, int page)
    {
        var placed = containerService.GetPlacedCardsOnPage(_containerId, page);
        HydrateMissingImageUris(placed);
        slots.Clear();
        for (var slot = 0; slot < SlotsPerPage; slot++)
            slots.Add(new BinderSlotItem(page, slot, placed.FirstOrDefault(c => c.Slot == slot)));
    }

    /// <summary>Fills in <see cref="CollectionCard.ImageUri"/> for cards that have none stored
    /// (e.g. prints whose Product never captured an art URL) by looking the card up in the game
    /// catalog — so a tile always falls back to the originally-downloaded art rather than showing
    /// a blank placeholder. Mirrors CollectionViewModel.HydrateMissingImageUris so the binder and
    /// the main Collection list render identical art; must run before the cards are bound, since
    /// <see cref="CollectionCard.ImageUri"/> isn't an observable property. Display-only — not
    /// persisted.</summary>
    private void HydrateMissingImageUris(IReadOnlyList<CollectionCard> cards)
    {
        foreach (var gameGroup in cards.Where(c => string.IsNullOrEmpty(c.ImageUri)).GroupBy(c => c.Game))
        {
            ICardGameService gameService;
            try { gameService = cardService.GetGameService(gameGroup.Key); }
            catch { continue; }

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

    partial void OnSpreadIndexChanged(int value) => Refresh();

    [RelayCommand(CanExecute = nameof(CanGoToNextSpread))]
    public void NextSpread()
    {
        if (CanGoToNextSpread) SpreadIndex++;
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousSpread))]
    public void PreviousSpread()
    {
        if (CanGoToPreviousSpread) SpreadIndex--;
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousSpread))]
    public void FirstSpread() => SpreadIndex = 0;

    [RelayCommand(CanExecute = nameof(CanGoToNextSpread))]
    public void LastSpread() => SpreadIndex = Math.Max(0, TotalSpreads - 1);

    /// <summary>Jump straight to a spread from the pagination strip.</summary>
    [RelayCommand]
    public void GoToSpread(int index)
    {
        if (index < 0 || index >= TotalSpreads) return;
        SpreadIndex = index;
    }

    [RelayCommand]
    public void AddPage()
    {
        containerService.AddBinderPage(_containerId);
        TotalPages++;
        SpreadIndex = TotalPages / 2; // spread containing the new last page
        Refresh();
    }

    public void DropOnSlot(int lotId, int page, int slot)
    {
        containerService.AssignCardToSlot(lotId, _containerId, page, slot);
        Refresh();
    }

    // --- Card context-menu actions (parity with the main Collection card list's context menu) ---
    // Both the Unplaced Cards pane and the binder-slot grid share this one command set. The view
    // points GetSelectedCards at whichever surface last showed its context menu (see BinderView's
    // PreviewMouseRightButtonDown handlers) and sets SelectionIsPlaced accordingly.

    /// <summary>Set by the view to whichever surface (Unplaced pane or slot grid) most recently
    /// opened a context menu — reused for every command below, the same delegate-indirection
    /// pattern CollectionViewModel uses for its own card list.</summary>
    public Func<IList<CollectionCard>>? GetSelectedCards { get; set; }

    /// <summary>Set by RootViewModel so a delete here also refreshes the dashboard/home tab.</summary>
    public Action? CollectionChanged { get; set; }

    [ObservableProperty]
    public partial int SelectedCardCount { get; set; }

    /// <summary>True when the current selection came from an occupied binder slot rather than the
    /// Unplaced Cards pane — gates "Remove from Binder Page", which only makes sense for a card
    /// that's actually placed.</summary>
    [ObservableProperty]
    public partial bool SelectionIsPlaced { get; set; }

    public bool HasSelection => SelectedCardCount > 0;
    public bool HasExactlyOneSelected => SelectedCardCount == 1;

    partial void OnSelectedCardCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasExactlyOneSelected));
        OpenCardEditorCommand.NotifyCanExecuteChanged();
        CopyCardNamesCommand.NotifyCanExecuteChanged();
        MoveToLocationCommand.NotifyCanExecuteChanged();
        ListForSaleCommand.NotifyCanExecuteChanged();
        UnlistForSaleCommand.NotifyCanExecuteChanged();
        MarkPickedCommand.NotifyCanExecuteChanged();
        SetConditionCommand.NotifyCanExecuteChanged();
        SetFoilCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        ListOnEbayCommand.NotifyCanExecuteChanged();
        ViewOnEbayCommand.NotifyCanExecuteChanged();
        EndEbayListingCommand.NotifyCanExecuteChanged();
        UnassignFromPageCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectionIsPlacedChanged(bool value) => UnassignFromPageCommand.NotifyCanExecuteChanged();

    /// <summary>Called by the view right before it shows the shared card context menu, from
    /// whichever surface (Unplaced pane or a binder slot) was right-clicked — points every
    /// selection-based command above at that surface's current selection.</summary>
    public void SetSelectionSource(Func<IList<CollectionCard>> getSelectedCards, bool isPlaced)
    {
        GetSelectedCards = getSelectedCards;
        SelectedCardCount = getSelectedCards().Count;
        SelectionIsPlaced = isPlaced;
    }

    /// <summary>Called when right-clicking empty space (an unoccupied slot) so stale selection
    /// state from a previous right-click doesn't leave context-menu actions looking enabled.</summary>
    public void ClearSelection()
    {
        GetSelectedCards = () => [];
        SelectedCardCount = 0;
        SelectionIsPlaced = false;
    }

    // --- Slot context (for "Add Missing Card...") ---
    // Captured when a binder slot is right-clicked (occupied or empty), so the card can be added
    // straight into that exact page/slot. Distinct from the card selection above, which is null for
    // an empty slot and absent entirely when the Unplaced pane is right-clicked.

    private int? _slotContextPage;
    private int? _slotContextSlot;

    /// <summary>True when the last right-click targeted a binder slot — gates the "Add Missing
    /// Card..." menu item (hidden when the Unplaced pane is right-clicked).</summary>
    [ObservableProperty]
    public partial bool HasSlotContext { get; set; }

    partial void OnHasSlotContextChanged(bool value) => AddMissingCardCommand.NotifyCanExecuteChanged();

    /// <summary>Records which binder slot was right-clicked, for "Add Missing Card...".</summary>
    public void SetSlotContext(int page, int slot)
    {
        _slotContextPage = page;
        _slotContextSlot = slot;
        HasSlotContext = true;
    }

    public void ClearSlotContext()
    {
        _slotContextPage = null;
        _slotContextSlot = null;
        HasSlotContext = false;
    }

    /// <summary>Opens the Add-Card dialog locked to the right-clicked slot; adding places the card
    /// there, displacing any existing card back to the Unplaced pool (swap).</summary>
    [RelayCommand(CanExecute = nameof(HasSlotContext))]
    public void AddMissingCard()
    {
        if (_slotContextPage is not int page || _slotContextSlot is not int slot) return;
        var result = dialogService.OpenManualAddToSlot(_containerId, page, slot);
        if (result == true) Refresh();
    }

    /// <summary>Resolve all real card IDs from selected rows (expands stacked entries, matching
    /// CollectionViewModel.GetAllSelectedCardIds — binder tiles aren't stacked today, but this
    /// keeps behavior identical if that ever changes).</summary>
    private List<int> GetAllSelectedCardIds()
    {
        var selected = GetSelectedCards?.Invoke();
        if (selected is null or { Count: 0 }) return [];
        return selected.SelectMany(c => c.StackedIds ?? [c.Id]).ToList();
    }

    [RelayCommand(CanExecute = nameof(HasExactlyOneSelected))]
    public void OpenCardEditor()
    {
        var card = GetSelectedCards?.Invoke()?.FirstOrDefault();
        if (card is null) return;
        var result = dialogService.EditCollectionCard(card);
        if (result.HasValue) Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void CopyCardNames()
    {
        var selected = GetSelectedCards?.Invoke();
        if (selected is null or { Count: 0 }) return;
        var names = string.Join(Environment.NewLine, selected.Select(c => c.Name));
        System.Windows.Clipboard.SetText(names);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void MoveToLocation()
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;

        var result = dialogService.PickMoveToLocation();
        if (result is null) return;

        cardService.MoveCardsToContainer(ids, result.Container.Id, result.Section);
        Refresh();
    }

    /// <summary>Backing list for the "Tags..." flyout — recomputed by <see cref="LoadTagFlyoutItems"/>
    /// immediately before the flyout's popup opens, so it reflects the current selection.</summary>
    public ObservableCollection<Controls.TagFlyoutItem> TagFlyoutItems { get; } = [];

    public void LoadTagFlyoutItems()
    {
        TagFlyoutItems.Clear();
        var selectedIds = GetAllSelectedCardIds();
        if (selectedIds.Count == 0) return;

        var tagsByLot = tagService.GetTagsByLots(selectedIds);
        foreach (var tag in tagService.GetAllTags())
        {
            var lotsWithTag = selectedIds.Count(id =>
                tagsByLot.TryGetValue(id, out var lotTags) && lotTags.Contains(tag.Name, StringComparer.OrdinalIgnoreCase));

            var state = Controls.TagTriState.Compute(lotsWithTag, selectedIds.Count);
            TagFlyoutItems.Add(new Controls.TagFlyoutItem(tag.Name, state));
        }
    }

    [RelayCommand]
    public void ToggleTagFlyoutItem((string Name, bool Apply) arg)
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;

        if (arg.Apply)
            tagService.AddTagToLots(ids, arg.Name);
        else
            tagService.RemoveTagFromLots(ids, arg.Name);

        Refresh();
    }

    [RelayCommand]
    public void CreateTagFlyoutItem(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return;

        ToggleTagFlyoutItem((trimmed, true));
        TagFlyoutItems.Add(new Controls.TagFlyoutItem(trimmed, Controls.TagCheckState.Checked));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void ListForSale()
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;

        var suggested = GetSelectedCards?.Invoke()?.FirstOrDefault()?.MarketPrice ?? 0m;
        var result = dialogService.PickListForSale(suggested);
        if (result is null) return;
        if (result.Quantity <= 0 || result.Price < 0) return;

        listingService.ListForSale(ids, result.Channel, result.Price, result.Quantity);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void UnlistForSale()
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        listingService.Unlist(ids);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void MarkPicked()
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        try
        {
            listingService.MarkPicked(ids);
            Refresh();
        }
        catch (InvalidOperationException)
        {
            // Same as the main list: some selected lots aren't eligible — silently skip.
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void SetCondition(string condition)
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        cardService.BulkUpdateField(ids, c => c.Condition = condition);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void SetFoil(string isFoilStr)
    {
        var isFoil = isFoilStr == "True";
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        cardService.BulkUpdateField(ids, c => c.IsFoil = isFoil);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void DeleteSelected()
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        foreach (var id in ids)
            cardService.DeleteCollectionCard(id);
        Refresh();
        CollectionChanged?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(HasExactlyOneSelected))]
    public void ListOnEbay()
    {
        var selected = GetSelectedCards?.Invoke();
        if (selected is null || selected.Count != 1) return;
        var card = selected[0];
        if (card.EbayListing?.Status == EbayListingStatus.Active) return;

        var result = dialogService.OpenEbayListingDialog(card);
        if (result == true) Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasExactlyOneSelected))]
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

    [RelayCommand(CanExecute = nameof(HasExactlyOneSelected))]
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

        var success = await ebayListingService.EndListingAsync(listing);
        if (success) Refresh();
    }

    /// <summary>Clears the selected card(s)' page/slot placement, returning them to the Unplaced
    /// Cards pool — the inverse of dragging a card onto a slot.</summary>
    [RelayCommand(CanExecute = nameof(SelectionIsPlaced))]
    public void UnassignFromPage()
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        foreach (var id in ids)
            containerService.UnassignFromPage(id);
        Refresh();
    }
}
