using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Collection;
using OmniCard.Models;
using MtgCard = OmniCard.Models.Card;

namespace OmniCard.Views.Root;

public sealed partial class CollectionViewModel
{
    /// <summary>A "group by" choice shown in the deck-box grouping selector.</summary>
    public sealed record DeckGroupingOption(string Label, DeckGroupAxis Axis);

    /// <summary>One rendered stack in the deck view: a heading plus the cards that fall in it.
    /// Rebuilt wholesale on every grouping change, so a plain list is enough.</summary>
    public sealed class DeckStackGroup(string header, IReadOnlyList<CollectionCard> cards)
    {
        public string Header { get; } = header;
        public IReadOnlyList<CollectionCard> Cards { get; } = cards;
        /// <summary>Total physical copies in the group (sums stacked-tile quantities).</summary>
        public int Count { get; } = cards.Sum(c => c.Quantity);
    }

    public IReadOnlyList<DeckGroupingOption> DeckGroupingOptions { get; } =
    [
        new("None", DeckGroupAxis.None),
        new("Type", DeckGroupAxis.Type),
        new("Mana Value", DeckGroupAxis.ManaValue),
    ];

    /// <summary>True while viewing a Deck Box location — gates the grouping selector (deck boxes only).</summary>
    [ObservableProperty]
    public partial bool IsCurrentLocationDeckBox { get; set; }

    private DeckGroupingOption? _selectedDeckGrouping;

    /// <summary>Current group-by axis for the deck view. Defaults to None (the flat tile grid).</summary>
    public DeckGroupingOption? SelectedDeckGrouping
    {
        get => _selectedDeckGrouping ??= DeckGroupingOptions[0];
        set
        {
            var next = value ?? DeckGroupingOptions[0];
            if (ReferenceEquals(next, _selectedDeckGrouping)) return;
            _selectedDeckGrouping = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDeckGroupingActive));
            _ = RefreshDeckGroupsAsync();
        }
    }

    /// <summary>The deck grouped-stacks view is showing (a deck box with a non-None axis) rather than
    /// the flat card list. The View swaps panels on this.</summary>
    public bool IsDeckGroupingActive =>
        IsCurrentLocationDeckBox && (SelectedDeckGrouping?.Axis ?? DeckGroupAxis.None) != DeckGroupAxis.None;

    public ObservableCollection<DeckStackGroup> DeckGroups { get; } = [];

    public const double MinDeckCardWidth = 96;
    public const double MaxDeckCardWidth = 340;

    /// <summary>Card width (px) in the grouped deck view — the zoom level, adjusted with Ctrl+wheel.</summary>
    [ObservableProperty]
    public partial double DeckCardWidth { get; set; } = 150;

    /// <summary>Nudge the deck-view zoom by <paramref name="delta"/> px, clamped to a sane range.</summary>
    public void AdjustDeckZoom(double delta) =>
        DeckCardWidth = Math.Clamp(DeckCardWidth + delta, MinDeckCardWidth, MaxDeckCardWidth);

    /// <summary>Tags the selected lot(s) with the reserved <c>commander</c> tag, so they float to the
    /// deck's Commander group (and mark the box as a Commander deck). Applied to the selection, so it
    /// also supports partner commanders (select both).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void SetAsCommander()
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        _tagService.AddTagToLots(ids, DeckCardClassifier.CommanderTag);
        ApplyTagToDisplayedSelection(DeckCardClassifier.CommanderTag, applied: true);
        ReportMessage?.Invoke($"Set {ids.Count} card(s) as commander.");
        _ = SearchCollection(); // re-groups so the card moves into the Commander pile
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void RemoveCommander()
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        _tagService.RemoveTagFromLots(ids, DeckCardClassifier.CommanderTag);
        ApplyTagToDisplayedSelection(DeckCardClassifier.CommanderTag, applied: false);
        ReportMessage?.Invoke($"Removed commander from {ids.Count} card(s).");
        _ = SearchCollection();
    }

    /// <summary>Reset grouping to None when leaving/entering a location so a stale axis from a prior
    /// deck box doesn't carry over. Called from the navigation commands.</summary>
    private void ResetDeckGrouping()
    {
        _selectedDeckGrouping = DeckGroupingOptions[0];
        OnPropertyChanged(nameof(SelectedDeckGrouping));
        OnPropertyChanged(nameof(IsDeckGroupingActive));
        DeckGroups.Clear();
    }

    /// <summary>Rebuild the grouped stacks after the flat list refreshes, but only when the deck
    /// view is active — otherwise a no-op.</summary>
    private void RefreshDeckGroupsIfActive()
    {
        if (IsDeckGroupingActive) _ = RefreshDeckGroupsAsync();
    }

    private int _deckGroupGeneration;

    /// <summary>Loads the full deck-box contents (a deck is small), hydrates the catalog type line
    /// and mana value, classifies each card onto the selected axis, and publishes ordered stacks.</summary>
    private async Task RefreshDeckGroupsAsync()
    {
        if (!IsDeckGroupingActive)
        {
            DeckGroups.Clear();
            return;
        }

        var axis = SelectedDeckGrouping!.Axis;
        var containerFilter = SelectedContainerFilter?.Id ?? CurrentLocationId;
        var query = CollectionSearchQuery;
        var game = GameFilter;
        var filterPreset = SelectedFilterPreset;
        var stacked = IsStacked;
        var generation = ++_deckGroupGeneration;

        var groups = await Task.Run(() =>
        {
            // Whole deck at once (no paging) — a deck box holds ~100 cards.
            var results = new ObservableCollection<CollectionCard>();
            _cardService.SearchCollection(query, game, containerFilter, sortPreset: null, filterPreset, stacked, 0, int.MaxValue, results);

            FetchBatchPrices(results);
            HydrateMissingImageUris(results);

            var statusByLot = _listingService.GetActiveListingStatusByLot(results.Select(c => c.Id));
            foreach (var card in results)
                card.ListingStatus = statusByLot.TryGetValue(card.Id, out var st) ? st : null;

            var tagsByLot = _tagService.GetTagsByLots(results.Select(c => c.Id));
            foreach (var card in results)
                card.Tags = tagsByLot.TryGetValue(card.Id, out var tags) ? tags : [];

            HydrateGroupingFields(results);

            return BuildGroups(results, axis);
        });

        if (generation != _deckGroupGeneration) return; // superseded by a newer refresh

        DeckGroups.Clear();
        foreach (var g in groups) DeckGroups.Add(g);
    }

    /// <summary>Populates <see cref="CollectionCard.TypeLine"/> / <see cref="CollectionCard.ManaValue"/>
    /// from each card's game catalog. Only MTG (Scryfall) exposes a type line and CMC; other games
    /// fall back to the lightweight stored <see cref="CollectionCard.CardType"/> for the type line.</summary>
    private void HydrateGroupingFields(IEnumerable<CollectionCard> cards)
    {
        foreach (var c in cards)
        {
            if (c.TypeLine is not null) continue; // already hydrated
            try
            {
                if (_cardService.GetGameService(c.Game).FindCardById(c.GameCardId) is MtgCard mtg)
                {
                    c.TypeLine = mtg.TypeLine;
                    c.ManaValue = mtg.Cmc;
                }
                else
                {
                    c.TypeLine = c.CardType;
                }
            }
            catch
            {
                c.TypeLine = c.CardType;
            }
        }
    }

    private static List<DeckStackGroup> BuildGroups(IEnumerable<CollectionCard> cards, DeckGroupAxis axis) =>
        cards
            .Select(c => (Card: c, Group: DeckCardClassifier.Classify(axis, c.TypeLine, c.ManaValue, c.Tags)))
            .GroupBy(x => x.Group.Key)
            .Select(g => (
                Header: g.Key,
                Order: g.Min(x => x.Group.SortOrder),
                Cards: g.Select(x => x.Card).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderBy(g => g.Order)
            .ThenBy(g => g.Header, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DeckStackGroup($"{g.Header} ({g.Cards.Sum(c => c.Quantity)})", g.Cards))
            .ToList();

    partial void OnIsCurrentLocationDeckBoxChanged(bool value) =>
        OnPropertyChanged(nameof(IsDeckGroupingActive));
}
