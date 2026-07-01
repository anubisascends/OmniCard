using System.Windows;

namespace OmniCard.Views.SortFilterBuilder;

public partial class SortFilterBuilderView : Window
{
    public SortFilterBuilderViewModel ViewModel { get; }

    public SortFilterBuilderView(SortFilterBuilderViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
