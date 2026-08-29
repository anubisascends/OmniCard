using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Binder;

/// <summary>One rendered slot in a binder page grid: its page/slot coordinates (for drag-and-drop
/// targeting) plus whatever card currently occupies it (null = empty slot). When the slot is empty
/// but the mirrored pocket on the reverse side of the physical sheet holds a card,
/// <see cref="HasCardOnReverse"/> is set so the view can show that game's card back (see
/// <see cref="ReverseGame"/>), with the reverse card's name as a tooltip.</summary>
public sealed record BinderSlotItem(
    int Page,
    int SlotIndex,
    CollectionCard? Card,
    bool HasCardOnReverse = false,
    CardGame? ReverseGame = null,
    string? ReverseCardName = null);

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

    /// <summary>The physical-sheet layout of the loaded binder — the source of truth for which
    /// logical page sits on the reverse of which, used to light up the card-back hint in empty
    /// pockets whose reverse pocket is filled. Re-read from the container service at the top of
    /// every <see cref="Refresh"/> so page mutations (add/insert/move/remove) don't leave it stale
    /// — otherwise reverse-side card backs never appear on newly added pages.</summary>
    private BinderSheetLayout? _sheetLayout;

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
        _sheetLayout = BinderSheetLayout.Parse(string.Join(",", layout.SheetSides), layout.TotalPages);
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

        // Re-read the sheet layout so reverse-side card backs stay correct after page mutations
        // (add/insert/move/remove) — the cached copy would otherwise be stale for new pages.
        var layout = containerService.GetBinderLayout(_containerId);
        _sheetLayout = BinderSheetLayout.Parse(string.Join(",", layout.SheetSides), layout.TotalPages);

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

        // Cards sitting in the pockets on the reverse side of this physical sheet, so an empty pocket
        // can show the back of the card behind it. No image hydration needed — we only draw a back.
        var reversePage = _sheetLayout?.ReversePageOf(page);
        var reverse = reversePage is int rp
            ? containerService.GetPlacedCardsOnPage(_containerId, rp)
            : [];

        slots.Clear();
        for (var slot = 0; slot < SlotsPerPage; slot++)
        {
            var card = placed.FirstOrDefault(c => c.Slot == slot);
            if (card is not null)
            {
                slots.Add(new BinderSlotItem(page, slot, card));
                continue;
            }

            // Empty pocket — light up a card-back hint if the mirrored reverse pocket is filled.
            var behind = CardBackAssets.ReverseCardFor(slot, Columns, SlotsPerPage, reverse);
            slots.Add(behind is null
                ? new BinderSlotItem(page, slot, null)
                : new BinderSlotItem(page, slot, null, HasCardOnReverse: true, behind.Game, behind.Name));
        }
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

    /// <summary>Adds a physical sheet to the end of the binder. <paramref name="mode"/> is
    /// <c>"single"</c> for a single-sided sheet (one page — a single-pocket page, or when the back
    /// isn't wanted); anything else adds the default double-sided sheet (front + back = two
    /// pages). Jumps to the spread containing the new last page.</summary>
    [RelayCommand]
    public void AddPage(string? mode)
    {
        var doubleSided = !string.Equals(mode, "single", StringComparison.OrdinalIgnoreCase);
        containerService.AddBinderSheet(_containerId, doubleSided);
        TotalPages = containerService.GetBinderLayout(_containerId).TotalPages;
        SpreadIndex = TotalPages / 2; // spread containing the new last page
        Refresh();
    }

    /// <summary>Opens the insert-position picker and inserts a new empty sheet where the user chose,
    /// shifting every later page (and its cards) up. Defaults the picker to the spread currently in
    /// view, and jumps to the inserted sheet afterward.</summary>
    [RelayCommand]
    public void InsertPage()
    {
        var nearPage = RightPageNumber ?? LeftPageNumber;
        if (dialogService.InsertBinderPage(_containerId, nearPage) is not { } result) return;

        containerService.InsertBinderSheet(_containerId, result.InsertIndex, result.DoubleSided);
        TotalPages = containerService.GetBinderLayout(_containerId).TotalPages;

        // Navigate to the spread that now shows the inserted sheet's first page.
        var sheets = containerService.GetSheets(_containerId);
        var insertedFirstPage = result.InsertIndex < sheets.Count ? sheets[result.InsertIndex].FirstPage : TotalPages;
        SpreadIndex = insertedFirstPage <= 1 ? 0 : insertedFirstPage / 2;
        Refresh();
    }

    /// <summary>Removes the physical sheet that owns <paramref name="pageNumber"/> — both sides of
    /// a double-sided sheet, so this removes two pages. Any cards on it return to the Unplaced pool
    /// and every later page shifts down. Confirms first (there's no undo) and refuses to remove the
    /// binder's only sheet.</summary>
    [RelayCommand]
    public void RemovePage(int? pageNumber)
    {
        if (pageNumber is not int page) return;

        BinderSheetInfo info;
        try { info = containerService.GetSheetForPage(_containerId, page); }
        catch (InvalidOperationException) { return; }

        if (info.TotalSheets <= 1)
        {
            System.Windows.MessageBox.Show(
                "A binder must keep at least one page. Delete the binder location itself if you want to remove it entirely.",
                "Remove Page",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var pagesClause = info.Sides == 2
            ? $"pages {info.FirstPage} and {info.FirstPage + 1} (front and back)"
            : $"page {info.FirstPage}";
        var cardsClause = info.CardCount == 0
            ? "It holds no cards."
            : $"Its {info.CardCount} card{(info.CardCount == 1 ? "" : "s")} will be returned to the Unplaced pool.";

        var confirm = System.Windows.MessageBox.Show(
            $"Remove this sheet — {pagesClause}? {cardsClause} Later pages shift down to close the gap. This can't be undone.",
            "Remove Page",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        containerService.RemoveBinderSheet(_containerId, page);
        TotalPages = containerService.GetBinderLayout(_containerId).TotalPages;
        if (SpreadIndex >= TotalSpreads) SpreadIndex = Math.Max(0, TotalSpreads - 1);
        Refresh();
    }

    /// <summary>Opens the move-destination picker for the sheet that owns <paramref name="pageNumber"/>
    /// and moves it there — like pulling a page out of the binder and slotting it in elsewhere. Every
    /// card on a shifted page moves with it (slots preserved). Jumps to the sheet's new location.</summary>
    [RelayCommand]
    public void MovePage(int? pageNumber)
    {
        if (pageNumber is not int page) return;

        BinderSheetInfo info;
        try { info = containerService.GetSheetForPage(_containerId, page); }
        catch (InvalidOperationException) { return; }

        if (info.TotalSheets <= 1)
        {
            System.Windows.MessageBox.Show(
                "There's only one page, so there's nowhere to move it.",
                "Move Page",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        if (dialogService.MoveBinderPage(_containerId, info.SheetIndex) is not int toIndex) return;

        containerService.MoveBinderSheet(_containerId, page, toIndex);

        // The moved sheet now sits at index `toIndex` in the reading order; jump to its spread.
        var sheets = containerService.GetSheets(_containerId);
        var landedFirstPage = toIndex < sheets.Count ? sheets[toIndex].FirstPage : sheets[^1].FirstPage;
        SpreadIndex = landedFirstPage <= 1 ? 0 : landedFirstPage / 2;
        Refresh();
    }

    /// <summary>Shifts the cards on <paramref name="pageNumber"/> — and, per the scope the user picks,
    /// the pages before or after it — toward the front or back by a chosen number of pages (slots
    /// preserved). For fixing an off-by-a-page data-entry mistake from a chosen page. Blocks (with a
    /// warning) if a card would fall off the binder's edge or collide with a card that isn't moving,
    /// rather than losing data.</summary>
    [RelayCommand]
    public void ShiftPage(int? pageNumber)
    {
        if (pageNumber is not int page) return;
        if (dialogService.ShiftBinderPage(page) is not { } choice || choice.DeltaPages == 0) return;

        try
        {
            containerService.ShiftPage(_containerId, page, choice.DeltaPages, choice.Scope);
        }
        catch (InvalidOperationException ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "Shift Cards",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

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
