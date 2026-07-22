using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

public partial class OrdersView : UserControl
{
    private Point _dragStart;
    private Order? _dragOrder;
    private OrdersViewModel? _wiredVm;

    public OrdersView() => InitializeComponent();

    private void OrdersView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not OrdersViewModel vm) return;

        if (!ReferenceEquals(_wiredVm, vm))
        {
            if (_wiredVm is not null) _wiredVm.PropertyChanged -= Vm_PropertyChanged;
            vm.PropertyChanged += Vm_PropertyChanged;
            _wiredVm = vm;
        }

        vm.Load();
        ApplyEditorLayout(vm);
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OrdersViewModel.SelectedOrder) or nameof(OrdersViewModel.IsEditorCollapsed)
            && DataContext is OrdersViewModel vm)
            ApplyEditorLayout(vm);
    }

    /// <summary>Drives the editor column width + splitter/handle visibility from VM state:
    /// open when a card is selected and not collapsed; a reopen handle when selected + collapsed;
    /// otherwise the board is full-width.</summary>
    private void ApplyEditorLayout(OrdersViewModel vm)
    {
        var open = vm.SelectedOrder is not null && !vm.IsEditorCollapsed;
        var canReopen = vm.SelectedOrder is not null && vm.IsEditorCollapsed;

        EditorColumn.MinWidth = open ? OrdersViewModel.MinEditorWidth : 0;
        EditorColumn.Width = open ? new GridLength(vm.EditorWidth) : new GridLength(0);
        EditorSplitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        EditorPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ExpandHandle.Visibility = canReopen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EditorSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is OrdersViewModel vm)
            vm.EditorWidth = EditorColumn.ActualWidth;
    }

    private void Card_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragOrder = (sender as FrameworkElement)?.Tag as Order;
    }

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragOrder is null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        DragDrop.DoDragDrop((DependencyObject)sender, _dragOrder, DragDropEffects.Move);
    }

    private void Column_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(Order)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Column_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Order)) is not Order order) return;
        if ((sender as FrameworkElement)?.Tag is not string statusText) return;
        if (!Enum.TryParse<OrderStatus>(statusText, out var target)) return;
        (DataContext as OrdersViewModel)?.MoveOrder(order, target);
        e.Handled = true;
    }

    private void Card_Select(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is OrdersViewModel vm && (sender as FrameworkElement)?.Tag is Order order)
            vm.SelectedOrder = order;
    }
}
