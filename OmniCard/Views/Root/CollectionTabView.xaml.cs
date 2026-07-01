using System.Windows;
using System.Windows.Controls;
using OmniCard.Models;

namespace OmniCard.Views.Root;

public partial class CollectionTabView : UserControl
{
    public RootViewModel? ViewModel { get; set; }

    public CollectionTabView()
    {
        InitializeComponent();
    }

    public void WireUp(CollectionViewModel vm)
    {
        CardList.WireUp(vm);
        BuildColumnChooser(vm);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CollectionViewModel.ColumnVisibility))
                BuildColumnChooser(vm);
        };
    }

    private void BuildColumnChooser(CollectionViewModel vm)
    {
        ColumnChooserList.Items.Clear();
        foreach (var kvp in vm.ColumnVisibility)
        {
            var cb = new CheckBox
            {
                Content = kvp.Key,
                IsChecked = kvp.Value,
                Margin = new Thickness(4, 2, 4, 2)
            };
            var column = kvp.Key;
            cb.Checked += (_, _) => vm.ToggleColumnVisibility(column);
            cb.Unchecked += (_, _) => vm.ToggleColumnVisibility(column);
            ColumnChooserList.Items.Add(cb);
        }
    }

    private void ColumnChooserLink_Click(object sender, RoutedEventArgs e)
    {
        ColumnChooserPopup.IsOpen = !ColumnChooserPopup.IsOpen;
    }

    public void FocusSearchBox()
    {
        CollectionSearchBox.Focus();
        CollectionSearchBox.SelectAll();
    }

    public void WireUpSealed(SealedProductViewModel vm)
    {
        SealedList.WireUp(vm);
    }

    public void SelectAll() => CardList.SelectAll();

    public IList<CollectionCard> GetSelectedCards() => CardList.GetSelectedCards();
}
