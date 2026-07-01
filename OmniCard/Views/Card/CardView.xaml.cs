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

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;

        var delta = e.Delta > 0 ? 0.1 : -0.1;
        ViewModel.Zoom = Math.Clamp(ViewModel.Zoom + delta, 0.5, 4.0);
        e.Handled = true;
    }
}
