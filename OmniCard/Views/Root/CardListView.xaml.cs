using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OmniCard.Models;

namespace OmniCard.Views.Root;

public partial class CardListView : UserControl
{
    public CollectionViewModel? ViewModel { get; set; }
    private ScrollViewer? _scrollViewer;

    public CardListView()
    {
        InitializeComponent();
    }

    public void WireUp(CollectionViewModel vm)
    {
        ViewModel = vm;
        DataContext = vm;

        // Hook scroll detection for incremental loading
        CollectionListBox.Loaded += (_, _) =>
        {
            if (_scrollViewer is not null)
                _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;

            _scrollViewer = FindVisualChild<ScrollViewer>(CollectionListBox);
            if (_scrollViewer is not null)
                _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
        };
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
        if (ViewModel is not null)
            ViewModel.SelectedCardCount = CollectionListBox.SelectedItems.Count;
    }

    // Right-clicking a tile selects it (unless it is part of an existing multi-selection),
    // so the context menu operates on the clicked card like the old DataGrid did.
    private void CollectionListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;

        var item = FindAncestor<ListBoxItem>(source);
        if (item is null) return;

        if (!item.IsSelected)
        {
            CollectionListBox.SelectedItems.Clear();
            item.IsSelected = true;
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

    public IList<CollectionCard> GetSelectedCards()
        => CollectionListBox.SelectedItems.Cast<CollectionCard>().ToList();
}
