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
    }

    public void FocusSearchBox()
    {
        CollectionSearchBox.Focus();
        CollectionSearchBox.SelectAll();
    }

    public void SelectAll() => CardList.SelectAll();

    public IList<CollectionCard> GetSelectedCards() => CardList.GetSelectedCards();
}
