using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OmniCard.Views.CollectionCardEditor;

public partial class CollectionCardEditorView : IView<CollectionCardEditorViewModel>
{
    private Point _panStart;
    private bool _isPanning;

    public CollectionCardEditorView(CollectionCardEditorViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        ViewModel.CloseDialog = result =>
        {
            DialogResult = result;
            Close();
        };
        DataContext = this;
    }

    public CollectionCardEditorViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = null;
        Close();
    }

    // --- Image zoom/pan ---

    private void Image_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;

        var scale = sv.Tag?.ToString() == "Scan" ? ScanImageScale : ApiImageScale;
        var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        var newScale = Math.Clamp(scale.ScaleX * factor, 1.0, 8.0);

        if (newScale <= 1.0)
        {
            // Reset to fit
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            sv.ScrollToHorizontalOffset(0);
            sv.ScrollToVerticalOffset(0);
        }
        else
        {
            // Zoom toward mouse position
            var mousePos = e.GetPosition(sv);
            var relX = (sv.HorizontalOffset + mousePos.X) / (sv.ExtentWidth > 0 ? sv.ExtentWidth : 1);
            var relY = (sv.VerticalOffset + mousePos.Y) / (sv.ExtentHeight > 0 ? sv.ExtentHeight : 1);

            scale.ScaleX = newScale;
            scale.ScaleY = newScale;

            sv.UpdateLayout();

            sv.ScrollToHorizontalOffset(relX * sv.ExtentWidth - mousePos.X);
            sv.ScrollToVerticalOffset(relY * sv.ExtentHeight - mousePos.Y);
        }

        e.Handled = true;
    }

    private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        _isPanning = true;
        _panStart = e.GetPosition(sv);
        sv.CaptureMouse();
        e.Handled = true;
    }

    private void Image_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        _isPanning = false;
        sv.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Image_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning || sender is not ScrollViewer sv) return;

        var pos = e.GetPosition(sv);
        var dx = _panStart.X - pos.X;
        var dy = _panStart.Y - pos.Y;
        _panStart = pos;

        sv.ScrollToHorizontalOffset(sv.HorizontalOffset + dx);
        sv.ScrollToVerticalOffset(sv.VerticalOffset + dy);
    }

    private void Image_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        var scale = sv.Tag?.ToString() == "Scan" ? ScanImageScale : ApiImageScale;
        scale.ScaleX = 1;
        scale.ScaleY = 1;
        sv.ScrollToHorizontalOffset(0);
        sv.ScrollToVerticalOffset(0);
        e.Handled = true;
    }

    // --- Search ---

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel.SearchCommand.CanExecute(null))
        {
            ViewModel.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SearchResult_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.AssignCardCommand.CanExecute(null))
            ViewModel.AssignCardCommand.Execute(null);
    }
}
