using System.Windows;
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
