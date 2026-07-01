using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using OmniCard.Models;

namespace OmniCard.Views.Root;

public partial class ScannerTabView : UserControl
{
    private readonly List<ScannedCard> _subscribedCards = [];

    public RootViewModel? ViewModel { get; set; }

    public ScannerTabView()
    {
        InitializeComponent();
    }

    public void WireUpAutoScroll()
    {
        if (ViewModel is null) return;
        ViewModel.CardService.ScannedCards.CollectionChanged += ScannedCards_CollectionChanged;

        // Restore persisted scanner list width
        if (ViewModel.ScannerListWidth > 0)
            ScannerListColumn.Width = new GridLength(ViewModel.ScannerListWidth, GridUnitType.Pixel);
    }

    private void ScannedCards_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            ViewModel?.OnScanCardAdded();

            // Subscribe to property changes on new cards for stats refresh
            if (e.NewItems is not null)
            {
                foreach (ScannedCard card in e.NewItems)
                {
                    card.PropertyChanged += ScannedCard_PropertyChanged;
                    _subscribedCards.Add(card);
                }
            }

            if (ViewModel?.CardService.ScannedCards.Count is not > 0)
                return;

            // Only auto-scroll if the user is already at the bottom
            if (!IsScrolledToBottom(ScannedCardsListView))
                return;

            // Defer to after layout so the new item is measured before scrolling
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                var sv = FindVisualChild<ScrollViewer>(ScannedCardsListView);
                sv?.ScrollToEnd();
            });
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove)
        {
            if (e.OldItems is not null)
            {
                foreach (ScannedCard card in e.OldItems)
                {
                    card.PropertyChanged -= ScannedCard_PropertyChanged;
                    _subscribedCards.Remove(card);
                }
            }
            ViewModel?.RefreshScanStats();
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var card in _subscribedCards)
                card.PropertyChanged -= ScannedCard_PropertyChanged;
            _subscribedCards.Clear();

            ViewModel?.RefreshScanStats();

            // After clear/commit, scroll to top
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                var sv = FindVisualChild<ScrollViewer>(ScannedCardsListView);
                sv?.ScrollToHome();
            });
        }
    }

    private void ScannedCard_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScannedCard.Match) or nameof(ScannedCard.FlagReason))
            ViewModel?.RefreshScanStats();
    }

    private static bool IsScrolledToBottom(ListView listView)
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(listView);
        if (scrollViewer is null)
            return true; // No scroll yet (list fits in view) — treat as "at bottom"

        // At bottom when scrolled to end, or content doesn't overflow
        return scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 1
            || scrollViewer.ScrollableHeight <= 0;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;
            var result = FindVisualChild<T>(child);
            if (result is not null)
                return result;
        }
        return null;
    }

    private void GridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.ScannerListWidth = ScannerListColumn.ActualWidth;
    }

    public void FocusManualSearchBox() => DetailPanel.FocusSearchBox();

    public void SelectAll() => ScannedCardsListView.SelectAll();

    private void ScannedCardsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView listView)
            ViewModel?.UpdateSelection(listView.SelectedItems.Cast<ScannedCard>().ToList());
    }

    private void SetFilterComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (ViewModel is null) return;

        ViewModel.SetSearchText = "";

        var textBox = SetFilterComboBox.Template.FindName("PART_EditableTextBox", SetFilterComboBox) as System.Windows.Controls.TextBox;
        textBox?.Focus();
        textBox?.SelectAll();
    }

    private void SetFilterComboBox_DropDownClosed(object? sender, EventArgs e)
    {
        if (ViewModel is null) return;

        ViewModel.SetSearchText = "";
        SetFilterComboBox.Text = ViewModel.SetFilterSummary;
    }
}
