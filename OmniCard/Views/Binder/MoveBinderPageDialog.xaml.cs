using System.Windows;

namespace OmniCard.Views.Binder;

public partial class MoveBinderPageDialog : Window
{
    public MoveBinderPageDialog(MoveBinderPageViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnMove(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
