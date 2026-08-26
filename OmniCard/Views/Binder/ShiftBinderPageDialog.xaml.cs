using System.Windows;

namespace OmniCard.Views.Binder;

public partial class ShiftBinderPageDialog : Window
{
    public ShiftBinderPageDialog(ShiftBinderPageViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnShift(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
