using System.Windows;

namespace OmniCard.Views.BinderAudit;

public partial class BinderAuditView : Window
{
    private readonly BinderAuditViewModel _viewModel;

    public BinderAuditViewModel ViewModel => _viewModel;

    public BinderAuditView(BinderAuditViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // Applying corrections closes the dialog; the caller then refreshes the collection/tiles.
        _viewModel.RequestClose = Close;
    }
}
