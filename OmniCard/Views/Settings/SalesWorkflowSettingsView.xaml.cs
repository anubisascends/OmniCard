using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OmniCard.Views.Settings;

public partial class SalesWorkflowSettingsView : UserControl
{
    private Point _dragStart;
    private LaneEditItem? _dragLane;

    public SalesWorkflowSettingsView() => InitializeComponent();

    private void Lane_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragLane = (sender as FrameworkElement)?.Tag as LaneEditItem;
    }

    private void Lane_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragLane is null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        // Don't start a drag when the gesture began inside an editable control (text box / combo);
        // that would hijack text selection. Only the row surface / drag handle initiates a reorder.
        if (e.OriginalSource is DependencyObject src && IsWithinEditableControl(src)) return;

        DragDrop.DoDragDrop((DependencyObject)sender, _dragLane, DragDropEffects.Move);
    }

    private void Lane_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(LaneEditItem)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Lane_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not SalesWorkflowSettingsViewModel vm) return;
        if (e.Data.GetData(typeof(LaneEditItem)) is not LaneEditItem dragged) return;
        if ((sender as FrameworkElement)?.Tag is not LaneEditItem target) return;
        e.Handled = true;
        if (ReferenceEquals(dragged, target)) return;

        var targetIndex = vm.Lanes.IndexOf(target);
        vm.MoveLane(dragged, targetIndex);
    }

    private static bool IsWithinEditableControl(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is TextBox or ComboBox) return true;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }
        return false;
    }
}
