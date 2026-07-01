using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OmniCard.Models;

namespace OmniCard.Views.Root;

public partial class CardListView : UserControl
{
    public CollectionViewModel? ViewModel { get; set; }

    public CardListView()
    {
        InitializeComponent();
    }

    public void WireUp(CollectionViewModel vm)
    {
        ViewModel = vm;
        DataContext = vm;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CollectionViewModel.ColumnVisibility)
                or nameof(CollectionViewModel.IsStacked))
                SyncColumnVisibility();
        };
        SyncColumnVisibility();
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
            "MarketPrice" => "PurchasePrice", // sort by purchase price as proxy
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
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(card.ImageUri);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 250;
                bmp.EndInit();
                bmp.Freeze();
                imageSource = bmp;
            }
            catch
            {
                imageSource = null;
            }
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
