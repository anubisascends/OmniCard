using System.Windows.Input;

namespace OmniCard.Views.Card;

public partial class CardView : IView<CardViewModel>
{
    public CardView(CardViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = this;
    }

    public CardViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
