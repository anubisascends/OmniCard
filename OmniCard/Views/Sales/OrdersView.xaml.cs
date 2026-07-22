using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

public partial class OrdersView : UserControl
{
    private Point _dragStart;
    private Order? _dragOrder;

    public OrdersView() => InitializeComponent();

    private void OrdersView_OnLoaded(object sender, RoutedEventArgs e) =>
        (DataContext as OrdersViewModel)?.Load();

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
