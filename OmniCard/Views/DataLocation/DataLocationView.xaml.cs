using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OmniCard.Views.DataLocation;

public partial class DataLocationView : Window, IView<DataLocationViewModel>
{
    public DataLocationView(DataLocationViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        ViewModel.CloseDialog = Close;
        DataContext = this;
    }

    public DataLocationViewModel ViewModel { get; }
    IViewModel IView.ViewModel => ViewModel;

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        await ViewModel.LoadAsync();
    }
}

public class InvertBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}
