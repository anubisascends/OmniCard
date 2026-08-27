using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OmniCard.Models;

namespace OmniCard.Views.Root;

public partial class CardListView : UserControl
{
    public CollectionViewModel? ViewModel { get; set; }
    private ScrollViewer? _scrollViewer;
    private RoutedEventHandler? _listBoxLoadedHandler;

    // Type-ahead ("jump to card by name") state. The buffer accumulates typed characters;
    // a short idle timeout resets it, matching Windows Explorer's behavior.
    private string _typeAheadBuffer = "";
    private readonly DispatcherTimer _typeAheadTimer;
    private static readonly TimeSpan TypeAheadTimeout = TimeSpan.FromSeconds(1);

    public CardListView()
    {
        InitializeComponent();

        _typeAheadTimer = new DispatcherTimer { Interval = TypeAheadTimeout };
        _typeAheadTimer.Tick += (_, _) => ResetTypeAhead();
    }

    public void WireUp(CollectionViewModel vm)
    {
        if (ViewModel is not null)
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

        ViewModel = vm;
        DataContext = vm;
        vm.PropertyChanged += ViewModel_PropertyChanged;

        // Hook scroll detection for incremental loading (unsubscribe first so repeated WireUp
        // calls don't accumulate handlers).
        if (_listBoxLoadedHandler is not null)
            CollectionListBox.Loaded -= _listBoxLoadedHandler;

        _listBoxLoadedHandler = (_, _) =>
        {
            if (_scrollViewer is not null)
                _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;

            _scrollViewer = FindVisualChild<ScrollViewer>(CollectionListBox);
            if (_scrollViewer is not null)
                _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
        };
        CollectionListBox.Loaded += _listBoxLoadedHandler;

        ApplySidebarLayout(vm);
    }

    // A new result set replaces CollectionSearchResults on every search/filter/sort. WPF keeps
    // the ScrollViewer's old vertical offset across the swap, so with virtualization the viewport
    // can sit past the replaced (often shorter) list, leaving it blank until the user scrolls up.
    // Reset to the top when the collection is replaced. LoadMore appends to the same instance
    // (no property change), so paging does not trigger this and the scroll position is preserved.
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CollectionViewModel.IsSidebarCollapsed) && ViewModel is not null)
        {
            ApplySidebarLayout(ViewModel);
            return;
        }

        // The deck stacks are rebuilt (new card instances) on every reload and when grouping
        // toggles — drop selection refs that point at the old set.
        if (e.PropertyName is nameof(CollectionViewModel.CollectionSearchResults)
            or nameof(CollectionViewModel.IsCurrentLocationDeckBox)
            or nameof(CollectionViewModel.IsDeckGroupingActive))
            ClearDeckSelection();

        if (e.PropertyName != nameof(CollectionViewModel.CollectionSearchResults))
            return;

        // Defer so the ItemsSource binding and layout update before we scroll.
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => _scrollViewer?.ScrollToTop()));
    }

    /// <summary>Drives the details panel's column width + splitter/handle visibility from VM
    /// state — open (at the persisted width) unless the user collapsed it, in which case only a
    /// reopen handle shows. Independent of card selection; the panel body just shows/hides its
    /// field rows based on <see cref="CollectionViewModel.SidebarFields"/>.</summary>
    private void ApplySidebarLayout(CollectionViewModel vm)
    {
        var open = !vm.IsSidebarCollapsed;

        SidebarColumn.MinWidth = open ? CollectionViewModel.MinSidebarWidth : 0;
        SidebarColumn.Width = open ? new GridLength(vm.SidebarWidth) : new GridLength(0);
        SidebarSplitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        SidebarPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        SidebarExpandHandle.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SidebarSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.SidebarWidth = SidebarColumn.ActualWidth;
    }

    private async void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv || ViewModel is null || !ViewModel.HasMoreResults)
            return;

        // Load more when scrolled within 20% of the bottom
        if (sv.VerticalOffset >= sv.ScrollableHeight * 0.8 && sv.ScrollableHeight > 0)
            await ViewModel.LoadMore();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var result = FindVisualChild<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    private void CollectionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null) return;

        ViewModel.SelectedCardCount = CollectionListBox.SelectedItems.Count;

        // Not driven off SelectedCardCount's change notification: swapping from one single-card
        // selection to another leaves the count at 1, so that notification wouldn't fire here.
        ViewModel.RebuildSidebarFields();
    }

    // Right-clicking a tile selects it (unless it is part of an existing multi-selection),
    // so the context menu operates on the clicked card like the old DataGrid did.
    private void CollectionListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;

        var item = FindAncestor<ListBoxItem>(source);
        if (item?.DataContext is not { } data) return;

        // VirtualizationMode=Recycling reuses ListBoxItem containers, so item.IsSelected can be
        // stale after the ItemsSource is swapped (e.g. by the refresh that follows a List/Unlist/
        // Mark-Picked, which clears SelectedItems). Consulting the container's IsSelected there
        // wrongly reports "already selected" and skips selecting, leaving SelectedItems empty so
        // the context-menu command operates on nothing. Consult the ListBox's actual selection
        // (by data item) instead, which is authoritative regardless of container recycling.
        if (!CollectionListBox.SelectedItems.Contains(data))
        {
            CollectionListBox.SelectedItems.Clear();
            CollectionListBox.SelectedItems.Add(data);
        }
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    public void SelectAll() => CollectionListBox.SelectAll();

    public void ClearSelection()
    {
        CollectionListBox.UnselectAll();
        ClearDeckSelection();
    }

    public IList<CollectionCard> GetSelectedCards()
        => ViewModel?.IsDeckGroupingActive == true
            ? _deckSelection.ToList()
            : CollectionListBox.SelectedItems.Cast<CollectionCard>().ToList();

    // ── Deck-box stacks: selection + activation ─────────────────────────────────────────────
    // The grouped deck view isn't a ListBox, so selection is tracked here and mirrored onto each
    // card's transient IsSelected (drives the tile highlight). GetSelectedCards returns this set
    // while grouping is active, so the shared context menu and sidebar operate on it unchanged.
    private readonly List<CollectionCard> _deckSelection = [];

    private void ClearDeckSelection()
    {
        foreach (var c in _deckSelection) c.IsSelected = false;
        _deckSelection.Clear();
    }

    private void SetDeckSelection(CollectionCard card)
    {
        ClearDeckSelection();
        card.IsSelected = true;
        _deckSelection.Add(card);
    }

    private void DeckStackCard_Pressed(object? sender, OmniCard.Controls.CardStackPressEventArgs e)
    {
        if (ViewModel is null) return;
        var card = e.Card;

        if (e.Button == System.Windows.Input.MouseButton.Right)
        {
            // Match the flat list: right-clicking outside the current selection reselects just that
            // card; right-clicking within it keeps the multi-selection. Don't handle the event, so
            // the ItemsControl's context menu still opens.
            if (!_deckSelection.Contains(card))
                SetDeckSelection(card);
        }
        else // left button
        {
            if (e.Ctrl)
            {
                if (_deckSelection.Remove(card)) card.IsSelected = false;
                else { card.IsSelected = true; _deckSelection.Add(card); }
            }
            else
            {
                SetDeckSelection(card);
            }
        }

        // Set before any double-click activation — CollectionCardDoubleClick reads SelectedCollectionCard.
        ViewModel.SelectedCollectionCard = _deckSelection.Count > 0 ? _deckSelection[^1] : null;
        ViewModel.SelectedCardCount = _deckSelection.Count;
        ViewModel.RebuildSidebarFields();

        if (e.Button == System.Windows.Input.MouseButton.Left && e.ClickCount == 2
            && ViewModel.CollectionCardDoubleClickCommand.CanExecute(null))
            ViewModel.CollectionCardDoubleClickCommand.Execute(null);
    }

    private void DeckGroupsScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ViewModel is null || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        ViewModel.AdjustDeckZoom(e.Delta > 0 ? 16 : -16);
        e.Handled = true; // consume so Ctrl+wheel zooms instead of scrolling
    }

    // ── Type-ahead: jump to a card by typing its name ──────────────────────────────────────
    // Matches against the name of currently-loaded cards (starts-with, case-insensitive) and
    // scrolls/selects the first hit. Typing more characters refines the prefix; repeating the
    // same single letter cycles through cards that start with it. Only loaded cards are searched
    // (results page in at PageSize) — the overlay hints when more are unloaded.

    private void CollectionListBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var text = e.Text;
        // Ignore control characters (Enter, Tab, Esc arrive here as text on some layouts).
        if (string.IsNullOrEmpty(text) || text.Length != 1 || char.IsControl(text[0]))
            return;

        // A leading space with an empty buffer is meaningless as a prefix — skip it.
        if (_typeAheadBuffer.Length == 0 && text[0] == ' ')
            return;

        char c = text[0];

        // Repeated same single letter → cycle through cards starting with that letter, rather
        // than extending the prefix to a string that matches nothing.
        bool cycle = _typeAheadBuffer.Length > 0
            && _typeAheadBuffer.All(ch => char.ToLowerInvariant(ch) == char.ToLowerInvariant(_typeAheadBuffer[0]))
            && char.ToLowerInvariant(c) == char.ToLowerInvariant(_typeAheadBuffer[0]);

        string candidate = cycle ? c.ToString() : _typeAheadBuffer + c;

        if (TryJump(candidate, cycle))
            _typeAheadBuffer = candidate;
        // On a miss the buffer is left untouched so the last good prefix is preserved.

        RestartTypeAheadTimer();
        e.Handled = true;
    }

    private void CollectionListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_typeAheadBuffer.Length == 0)
            return;

        if (e.Key == Key.Escape)
        {
            ResetTypeAhead();
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            var trimmed = _typeAheadBuffer[..^1];
            if (trimmed.Length == 0)
            {
                ResetTypeAhead();
            }
            else
            {
                TryJump(trimmed, cycle: false);
                _typeAheadBuffer = trimmed;
                RestartTypeAheadTimer();
            }
            e.Handled = true;
        }
    }

    /// <summary>Finds the first loaded card whose name starts with <paramref name="prefix"/> and
    /// scrolls/selects it. In cycle mode the search starts after the current selection and wraps.
    /// Returns false (leaving selection unchanged) when nothing matches.</summary>
    private bool TryJump(string prefix, bool cycle)
    {
        var results = ViewModel?.CollectionSearchResults;
        if (results is null || results.Count == 0)
            return false;

        int start = cycle ? CollectionListBox.SelectedIndex + 1 : 0;
        int match = -1;

        for (int i = 0; i < results.Count; i++)
        {
            int idx = (start + i) % results.Count;
            if (results[idx].Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                match = idx;
                break;
            }
        }

        if (match < 0)
            return false;

        CollectionListBox.SelectedIndex = match;
        CollectionListBox.ScrollIntoView(results[match]);
        UpdateTypeAheadOverlay(prefix);
        return true;
    }

    private void UpdateTypeAheadOverlay(string prefix)
    {
        TypeAheadText.Text = prefix;
        TypeAheadHint.Text = ViewModel?.HasMoreResults == true ? "(loaded cards only)" : "";
        TypeAheadOverlay.Visibility = Visibility.Visible;
    }

    private void RestartTypeAheadTimer()
    {
        _typeAheadTimer.Stop();
        _typeAheadTimer.Start();
    }

    private void ResetTypeAhead()
    {
        _typeAheadTimer.Stop();
        _typeAheadBuffer = "";
        TypeAheadOverlay.Visibility = Visibility.Collapsed;
    }

    private void TagsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;

        ViewModel.LoadTagFlyoutItems();
        // A Popup auto-closes when its PlacementTarget becomes invisible. The clicked MenuItem
        // goes invisible the instant its owning ContextMenu closes (which happens right after
        // this Click handler runs), so anchoring to it made the popup close itself immediately.
        // Anchor to the always-visible ListBox instead, positioned at the cursor.
        // Anchor to whichever surface is actually visible (the ListBox is collapsed in deck mode).
        TagsPopup.PlacementTarget = ViewModel.IsDeckGroupingActive ? DeckGroupsScroll : CollectionListBox;
        TagsPopup.IsOpen = true;
    }
}
