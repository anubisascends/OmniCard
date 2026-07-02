using System.Globalization;
using System.Windows;
using System.Windows.Data;
using OmniCard.Models;

namespace OmniCard.Views.StorageManager;

public partial class StorageManagerView : Window, IView<StorageManagerViewModel>
{
    public static readonly IReadOnlyList<ContainerType> ContainerTypes =
    [
        ContainerType.Binder,
        ContainerType.Box,
        ContainerType.DeckBox,
        ContainerType.DisplayCase,
    ];

    public StorageManagerView(StorageManagerViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        ViewModel.CloseDialog = Close;
        DataContext = this;
        ViewModel.Load();
    }

    public StorageManagerViewModel ViewModel { get; }
    IViewModel IView.ViewModel => ViewModel;
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
