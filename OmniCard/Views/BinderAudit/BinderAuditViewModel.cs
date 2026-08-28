using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.CardMatching;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Binder;

namespace OmniCard.Views.BinderAudit;

/// <summary>What the user marked a single pocket as during a visual binder audit.</summary>
public enum AuditMark
{
    /// <summary>Not yet reviewed.</summary>
    None,

    /// <summary>Occupied pocket the user confirmed is correct — no correction applied.</summary>
    Correct,

    /// <summary>Occupied pocket whose card is physically absent — flag the lot missing.</summary>
    Missing,

    /// <summary>Occupied pocket holding the wrong card/printing — reassign to the right one.</summary>
    Wrong,

    /// <summary>Empty pocket that physically holds a card the app doesn't know about — add it.</summary>
    ExtraPresent,
}

/// <summary>One rendered pocket in the read-only binder-audit grid: its page/slot coordinates, the
/// card the app records there (null = empty pocket), the reverse-side card-back hint (mirrors
/// <c>BinderSlotItem</c>), and the <see cref="AuditMark"/> the user has applied.</summary>
public sealed partial class BinderAuditSlotItem(
    int page,
    int slotIndex,
    CollectionCard? card,
    bool hasCardOnReverse = false,
    CardGame? reverseGame = null,
    string? reverseCardName = null) : ObservableObject
{
    public int Page { get; } = page;
    public int SlotIndex { get; } = slotIndex;
    public CollectionCard? Card { get; } = card;
    public bool HasCardOnReverse { get; } = hasCardOnReverse;
    public CardGame? ReverseGame { get; } = reverseGame;
    public string? ReverseCardName { get; } = reverseCardName;

    public bool IsOccupied => Card is not null;

    [ObservableProperty]
    public partial AuditMark Mark { get; set; }
}

/// <summary>A flagged pocket surfaced in the review-and-apply step. Missing rows are informational;
/// Wrong and Extra rows require the user to pick the correct card from a catalog search.</summary>
public sealed partial class BinderAuditReviewRow : ObservableObject
{
    public required AuditMark Mark { get; init; }
    public required int Page { get; init; }
    public required int Slot { get; init; }

    /// <summary>The card the app currently records in this pocket — set for Missing/Wrong, null for Extra.</summary>
    public CollectionCard? Card { get; init; }

    /// <summary>Game to search when reassigning (Wrong) or adding (Extra).</summary>
    public CardGame SearchGame { get; init; }

    public string PocketLabel { get; init; } = "";
    public string CurrentLabel { get; init; } = "";

    public string MarkLabel => Mark switch
    {
        AuditMark.Missing => "Missing",
        AuditMark.Wrong => "Wrong card",
        AuditMark.ExtraPresent => "Extra card",
        _ => "",
    };

    /// <summary>Wrong/Extra rows can't be applied until the user has chosen a replacement.</summary>
    public bool RequiresSelection => Mark is AuditMark.Wrong or AuditMark.ExtraPresent;

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial List<CardMatch>? SearchResults { get; set; }

    [ObservableProperty]
    public partial CardMatch? SelectedMatch { get; set; }
}

/// <summary>Read-only visual audit of a binder. Renders the binder spread by spread (no drag/drop,
/// no editing), lets the user tag each pocket — Correct / Missing / Wrong for filled pockets, and
/// Extra for empty ones — then reviews the flagged pockets and applies the corrections in one pass
/// (flag missing, reassign a wrong printing, add an extra card). Mirrors the read-only subset of
/// <see cref="Binder.BinderViewModel"/> for the spread math and page fill.</summary>
public sealed partial class BinderAuditViewModel(
    IStorageContainerService containerService,
    ICardService cardService,
    IDataPathService dataPathService) : ViewModel
{
    public string DataDirectory => dataPathService.DataDirectory;

    public ObservableCollection<BinderAuditSlotItem> LeftPageSlots { get; } = [];
    public ObservableCollection<BinderAuditSlotItem> RightPageSlots { get; } = [];

    /// <summary>The marks the user has applied, keyed by (page, slot), so they survive spread
    /// navigation and can be gathered for the review step. Carries the recorded card too, for
    /// building Missing/Wrong review rows without re-querying.</summary>
    private readonly Dictionary<(int Page, int Slot), (AuditMark Mark, CollectionCard? Card)> _marks = [];

    [ObservableProperty]
    public partial string ContainerName { get; set; } = "";

    [ObservableProperty]
    public partial int SpreadIndex { get; set; }

    [ObservableProperty]
    public partial int TotalPages { get; set; } = 1;

    [ObservableProperty]
    public partial int SlotsPerPage { get; set; } = 9;

    [ObservableProperty]
    public partial int Columns { get; set; } = 3;

    [ObservableProperty]
    public partial bool IsReviewMode { get; set; }

    /// <summary>Rows shown in the review-and-apply step, one per flagged (non-correct) pocket.</summary>
    public ObservableCollection<BinderAuditReviewRow> ReviewRows { get; } = [];

    private int _containerId;
    private BinderSheetLayout? _sheetLayout;
    private CardGame _binderGame = CardGame.Mtg;
    private int _totalPlacedCount;

    // --- Spread geometry (read-only subset of BinderViewModel) -----------------------------------

    public int? LeftPageNumber => SpreadIndex == 0 ? null : SpreadIndex * 2;
    public int? RightPageNumber => SpreadIndex == 0 ? 1 : (SpreadIndex * 2 + 1 <= TotalPages ? SpreadIndex * 2 + 1 : null);
    public bool HasLeftPage => LeftPageNumber is not null;
    public bool HasRightPage => RightPageNumber is not null;

    public string PageRangeLabel => HasLeftPage
        ? (HasRightPage ? $"Pages {LeftPageNumber}-{RightPageNumber}" : $"Page {LeftPageNumber}")
        : $"Page {RightPageNumber}";

    private int TotalSpreads => 1 + TotalPages / 2;
    public bool CanGoToPreviousSpread => SpreadIndex > 0;
    public bool CanGoToNextSpread => SpreadIndex < TotalSpreads - 1;

    public ObservableCollection<BinderSpreadTab> SpreadTabs { get; } = [];

    private const int SlotWidth = 172;
    private const int SlotHeight = 240;
    public int PageWidth => Columns * SlotWidth;
    public int PageHeight => (int)Math.Ceiling((double)SlotsPerPage / Math.Max(Columns, 1)) * SlotHeight;

    // --- Progress readout ------------------------------------------------------------------------

    public int MarkedCount => _marks.Count(m => m.Value.Mark != AuditMark.None);
    public string AuditProgress => $"{MarkedCount} of {_totalPlacedCount} pockets reviewed";
    public int FlaggedCount => _marks.Count(m => m.Value.Mark is AuditMark.Missing or AuditMark.Wrong or AuditMark.ExtraPresent);

    // --- Loading ---------------------------------------------------------------------------------

    public void Load(int containerId)
    {
        _containerId = containerId;
        _marks.Clear();

        var container = containerService.GetAll().FirstOrDefault(c => c.Id == containerId);
        ContainerName = container?.Name ?? "";

        var layout = containerService.GetBinderLayout(containerId);
        SlotsPerPage = layout.SlotsPerPage;
        TotalPages = layout.TotalPages;
        Columns = layout.Columns;
        _sheetLayout = BinderSheetLayout.Parse(string.Join(",", layout.SheetSides), layout.TotalPages);
        SpreadIndex = 0;

        // Total occupied pockets across the whole binder (for the progress readout) and the binder's
        // dominant game (used to scope the catalog search when adding an Extra card to a blank pocket).
        _totalPlacedCount = 0;
        var gameTally = new Dictionary<CardGame, int>();
        for (var page = 1; page <= TotalPages; page++)
        {
            foreach (var c in containerService.GetPlacedCardsOnPage(containerId, page))
            {
                _totalPlacedCount++;
                gameTally[c.Game] = gameTally.GetValueOrDefault(c.Game) + 1;
            }
        }
        _binderGame = gameTally.Count > 0
            ? gameTally.OrderByDescending(kv => kv.Value).First().Key
            : cardService.SelectedGame;

        RebuildSpreadTabs();
        Refresh();
        NotifyProgress();
    }

    private void NotifyProgress()
    {
        OnPropertyChanged(nameof(MarkedCount));
        OnPropertyChanged(nameof(AuditProgress));
        OnPropertyChanged(nameof(FlaggedCount));
    }

    // --- Rendering -------------------------------------------------------------------------------

    private void Refresh()
    {
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
    }

    private void FillPage(ObservableCollection<BinderAuditSlotItem> slots, int page)
    {
        var placed = containerService.GetPlacedCardsOnPage(_containerId, page);
        HydrateMissingImageUris(placed);

        var reversePage = _sheetLayout?.ReversePageOf(page);
        var reverse = reversePage is int rp
            ? containerService.GetPlacedCardsOnPage(_containerId, rp)
            : [];

        slots.Clear();
        for (var slot = 0; slot < SlotsPerPage; slot++)
        {
            var card = placed.FirstOrDefault(c => c.Slot == slot);
            BinderAuditSlotItem item;
            if (card is not null)
            {
                item = new BinderAuditSlotItem(page, slot, card);
            }
            else
            {
                var behind = CardBackAssets.ReverseCardFor(slot, Columns, SlotsPerPage, reverse);
                item = behind is null
                    ? new BinderAuditSlotItem(page, slot, null)
                    : new BinderAuditSlotItem(page, slot, null, hasCardOnReverse: true, behind.Game, behind.Name);
            }

            // Restore any mark the user already applied to this pocket this session.
            if (_marks.TryGetValue((page, slot), out var recorded))
                item.Mark = recorded.Mark;

            slots.Add(item);
        }
    }

    /// <summary>Fills in missing art URLs from the game catalog so tiles render identically to the
    /// main binder view. Mirrors <c>BinderViewModel.HydrateMissingImageUris</c>. Display-only.</summary>
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
                try { card.ImageUri = CardImageUriResolver.From(gameService.FindCardById(card.GameCardId)); }
                catch { /* leave null; tile falls back to a placeholder */ }
            }
        }
    }

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
    partial void OnSpreadIndexChanged(int value) => Refresh();
    partial void OnSlotsPerPageChanged(int value) => OnPropertyChanged(nameof(PageHeight));

    partial void OnColumnsChanged(int value)
    {
        OnPropertyChanged(nameof(PageWidth));
        OnPropertyChanged(nameof(PageHeight));
    }

    // --- Navigation ------------------------------------------------------------------------------

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

    [RelayCommand]
    public void GoToSpread(int index)
    {
        if (index < 0 || index >= TotalSpreads) return;
        SpreadIndex = index;
    }

    // --- Marking ---------------------------------------------------------------------------------

    [RelayCommand]
    public void MarkCorrect(BinderAuditSlotItem? slot) => ApplyMark(slot, AuditMark.Correct, occupied: true);

    [RelayCommand]
    public void MarkMissing(BinderAuditSlotItem? slot) => ApplyMark(slot, AuditMark.Missing, occupied: true);

    [RelayCommand]
    public void MarkWrong(BinderAuditSlotItem? slot) => ApplyMark(slot, AuditMark.Wrong, occupied: true);

    [RelayCommand]
    public void MarkExtra(BinderAuditSlotItem? slot) => ApplyMark(slot, AuditMark.ExtraPresent, occupied: false);

    /// <summary>Applies (or toggles off) a mark on a pocket. <paramref name="occupied"/> gates the
    /// mark to the right kind of pocket — Correct/Missing/Wrong only on filled pockets, Extra only on
    /// empty ones. Tapping the same mark again clears it.</summary>
    private void ApplyMark(BinderAuditSlotItem? slot, AuditMark mark, bool occupied)
    {
        if (slot is null) return;
        if (slot.IsOccupied != occupied) return;

        var key = (slot.Page, slot.SlotIndex);
        if (slot.Mark == mark)
        {
            slot.Mark = AuditMark.None;
            _marks.Remove(key);
        }
        else
        {
            slot.Mark = mark;
            _marks[key] = (mark, slot.Card);
        }
        NotifyProgress();
    }

    // --- Review & apply --------------------------------------------------------------------------

    [RelayCommand]
    public void BeginReview()
    {
        foreach (var row in ReviewRows)
            row.PropertyChanged -= ReviewRowChanged;
        ReviewRows.Clear();

        foreach (var ((page, slot), (mark, card)) in _marks
                     .Where(m => m.Value.Mark is AuditMark.Missing or AuditMark.Wrong or AuditMark.ExtraPresent)
                     .OrderBy(m => m.Key.Page).ThenBy(m => m.Key.Slot))
        {
            var pocket = $"Page {page}, pocket {slot + 1}";
            var row = mark switch
            {
                AuditMark.Missing => new BinderAuditReviewRow
                {
                    Mark = mark, Page = page, Slot = slot, Card = card, SearchGame = card?.Game ?? _binderGame,
                    PocketLabel = pocket, CurrentLabel = DescribeCard(card),
                },
                AuditMark.Wrong => new BinderAuditReviewRow
                {
                    Mark = mark, Page = page, Slot = slot, Card = card, SearchGame = card?.Game ?? _binderGame,
                    PocketLabel = pocket, CurrentLabel = DescribeCard(card),
                },
                _ => new BinderAuditReviewRow // ExtraPresent
                {
                    Mark = mark, Page = page, Slot = slot, Card = null, SearchGame = _binderGame,
                    PocketLabel = pocket, CurrentLabel = "(empty pocket)",
                },
            };
            row.PropertyChanged += ReviewRowChanged;
            ReviewRows.Add(row);
        }

        IsReviewMode = true;
        ApplyCorrectionsCommand.NotifyCanExecuteChanged();
    }

    private static string DescribeCard(CollectionCard? card) => card is null
        ? ""
        : string.IsNullOrEmpty(card.SetCode) ? card.Name : $"{card.Name} · {card.SetCode.ToUpperInvariant()} #{card.Number}";

    private void ReviewRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BinderAuditReviewRow.SelectedMatch))
            ApplyCorrectionsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void ExitReview() => IsReviewMode = false;

    /// <summary>Runs the manual catalog search for a review row (Wrong/Extra). Bound to Enter in the
    /// row's search box, mirroring AuditReportViewModel.SearchForAssignment.</summary>
    [RelayCommand]
    public void SearchRow(BinderAuditReviewRow? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.SearchQuery)) return;
        row.SearchResults = cardService.GetGameService(row.SearchGame).SearchCards(row.SearchQuery, 20);
    }

    private bool CanApplyCorrections() =>
        ReviewRows.Count > 0 && ReviewRows.All(r => !r.RequiresSelection || r.SelectedMatch is not null);

    [RelayCommand(CanExecute = nameof(CanApplyCorrections))]
    public void ApplyCorrections()
    {
        foreach (var row in ReviewRows)
        {
            switch (row.Mark)
            {
                case AuditMark.Missing when row.Card is not null:
                    cardService.SetCardMissing(row.Card.Id);
                    break;

                case AuditMark.Wrong when row.Card is not null && row.SelectedMatch is { } wrongMatch:
                    var card = row.Card;
                    card.Name = wrongMatch.Name;
                    card.SetCode = wrongMatch.SetCode;
                    card.SetName = wrongMatch.SetName;
                    card.Number = wrongMatch.CollectorNumber;
                    card.GameCardId = wrongMatch.GameSpecificId;
                    card.ImageUri = wrongMatch.ImageUri;
                    card.Rarity = wrongMatch.Rarity;
                    card.Color = CardAttributeExtractor.ExtractColor(wrongMatch, card.Game);
                    card.CardType = CardAttributeExtractor.ExtractCardType(wrongMatch, card.Game);
                    cardService.UpdateCollectionCard(card);
                    break;

                case AuditMark.ExtraPresent when row.SelectedMatch is { } extraMatch:
                    cardService.AddMissingCardToSlot(
                        extraMatch, row.SearchGame, "NM", isFoil: false, foilType: null,
                        purchasePrice: null, _containerId, row.Page, row.Slot);
                    break;
            }
        }

        RequestClose?.Invoke();
    }

    /// <summary>Set by the view to close the dialog once corrections are applied.</summary>
    public Action? RequestClose { get; set; }
}
