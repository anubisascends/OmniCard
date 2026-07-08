using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OmniCard.Controls.Converters;
using OmniCard.Imaging;
using OmniCard.Models;

namespace OmniCard.Views.Root;

public partial class CardListView : UserControl
{
    public CollectionViewModel? ViewModel { get; set; }
    private PropertyChangedEventHandler? _vmHandler;
    private ScrollViewer? _scrollViewer;

    public CardListView()
    {
        InitializeComponent();
    }

    public void WireUp(CollectionViewModel vm)
    {
        if (ViewModel is not null && _vmHandler is not null)
            ViewModel.PropertyChanged -= _vmHandler;

        ViewModel = vm;
        DataContext = vm;
        _vmHandler = (_, e) =>
        {
            if (e.PropertyName is nameof(CollectionViewModel.ColumnVisibility)
                or nameof(CollectionViewModel.IsStacked))
                SyncColumnVisibility();
        };
        vm.PropertyChanged += _vmHandler;
        SyncColumnVisibility();

        // Hook scroll detection for incremental loading
        CollectionDataGrid.Loaded += (_, _) =>
        {
            if (_scrollViewer is not null)
                _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;

            _scrollViewer = FindVisualChild<ScrollViewer>(CollectionDataGrid);
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

    private void SyncColumnVisibility()
    {
        if (ViewModel is null) return;
        foreach (var col in CollectionDataGrid.Columns)
        {
            var key = ColumnTag.GetKey(col);
            if (key == "Quantity")
            {
                col.Visibility = ViewModel.IsStacked ? Visibility.Visible : Visibility.Collapsed;
                continue;
            }
            if (key is not null && ViewModel.ColumnVisibility.TryGetValue(key, out var visible))
                col.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void CollectionDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.SelectedCardCount = CollectionDataGrid.SelectedItems.Count;
    }

    public void SelectAll() => CollectionDataGrid.SelectAll();

    public IList<CollectionCard> GetSelectedCards()
        => CollectionDataGrid.SelectedItems.Cast<CollectionCard>().ToList();

    private void CollectionColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not DataGridColumnHeader header) return;
        if (header.Column is null) return;
        var key = ColumnTag.GetKey(header.Column);
        if (key is null) return;

        // Map column tags to sort field names
        var sortField = key switch
        {
            "Name" => "Name",
            "Set" => "SetName",
            "Number" => "Number",
            "Type" => "CardType",
            "Rarity" => "Rarity",
            "Finish" => "IsFoil",
            "MarketPrice" => "MarketPrice",
            "Condition" => "Condition",
            "PurchasePrice" => "PurchasePrice",
            "DateAdded" => "DateAdded",
            "Game" => "Game",
            _ => null
        };

        if (sortField is null) return;

        var isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        ViewModel?.ApplyColumnSort(isShift ? $"+{sortField}" : sortField);

        e.Handled = true;
    }

    private static readonly CardPreviewImageConverter _previewConverter = new();

    private void CollectionDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        if (ViewModel is null || e.Row.Item is not CollectionCard card) return;

        // Market price (cheap dictionary lookup)
        if (ViewModel.MarketPrices.TryGetValue(card.Id, out var price))
            e.Row.Tag = price > 0 ? $"{price:F2}" : "";

        // Set placeholder tooltip — actual image loaded lazily on hover
        e.Row.ToolTip = " ";
        e.Row.ToolTipOpening -= Row_ToolTipOpening;
        e.Row.ToolTipOpening += Row_ToolTipOpening;
    }

    private void Row_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not DataGridRow row || row.Item is not CollectionCard card || ViewModel is null)
        {
            e.Handled = true;
            return;
        }

        // Already loaded for this row?
        if (row.ToolTip is ToolTip) return;

        // In stacked mode, always use API card art; otherwise try scan image first
        ImageSource? imageSource;
        if (ViewModel.IsStacked && card.ImageUri is not null)
        {
            imageSource = CardArtCache.Instance?.GetImage(null, card.ImageUri);
        }
        else
        {
            imageSource = _previewConverter.Convert(card, typeof(ImageSource), ViewModel.DataDirectory, CultureInfo.InvariantCulture) as ImageSource;
        }

        if (imageSource is not null)
        {
            row.ToolTip = new ToolTip
            {
                Content = new Image
                {
                    Source = imageSource,
                    Width = 250,
                    Height = 350,
                    Stretch = Stretch.Uniform
                }
            };
        }
        else
        {
            e.Handled = true;
        }
    }
}
