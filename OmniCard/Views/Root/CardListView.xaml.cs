using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    public CardListView()
    {
        InitializeComponent();
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

    public void ClearSelection() => CollectionListBox.UnselectAll();

    public IList<CollectionCard> GetSelectedCards()
        => CollectionListBox.SelectedItems.Cast<CollectionCard>().ToList();

    private void TagsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;

        ViewModel.LoadTagFlyoutItems();
        // A Popup auto-closes when its PlacementTarget becomes invisible. The clicked MenuItem
        // goes invisible the instant its owning ContextMenu closes (which happens right after
        // this Click handler runs), so anchoring to it made the popup close itself immediately.
        // Anchor to the always-visible ListBox instead, positioned at the cursor.
        TagsPopup.PlacementTarget = CollectionListBox;
        TagsPopup.IsOpen = true;
    }
}
