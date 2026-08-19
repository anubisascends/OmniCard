using System.Windows;

namespace OmniCard.Views.Binder;

public partial class InsertBinderPageDialog : Window
{
    public InsertBinderPageDialog(InsertBinderPageViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnInsert(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
