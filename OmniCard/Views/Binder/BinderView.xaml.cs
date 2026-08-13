using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using OmniCard.Models;

namespace OmniCard.Views.Binder;

/// <summary>Drag payload carrying the dragged lot and, when dragged from an already-placed slot,
/// the page/slot it came from — null when dragged from the unplaced pool (left pane).</summary>
internal sealed record BinderDragPayload(int LotId, int? OriginPage, int? OriginSlot);

public partial class BinderView : Window
{
    private Point _dragStart;
    private BinderDragPayload? _dragPayload;
    private readonly BinderViewModel _viewModel;

    public BinderViewModel ViewModel => _viewModel;

    public BinderView(BinderViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    // The Unplaced pool's filter is now server-side (Scryfall syntax) — the box binds to
    // BinderViewModel.UnplacedFilterQuery, which re-queries the pool. No client-side predicate here.

    private void UnplacedCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var card = (sender as FrameworkElement)?.Tag as CollectionCard;

        if (e.ClickCount == 2 && card is not null)
        {
            _dragPayload = null;
            _viewModel?.SetSelectionSource(() => new List<CollectionCard> { card }, isPlaced: false);
            _viewModel?.OpenCardEditorCommand.Execute(null);
            return;
        }

        _dragStart = e.GetPosition(null);
        _dragPayload = card is not null ? new BinderDragPayload(card.Id, null, null) : null;
    }

    private void UnplacedCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragPayload is null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        DragDrop.DoDragDrop((DependencyObject)sender, _dragPayload, DragDropEffects.Move);
    }

    private void SlotCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var item = (sender as FrameworkElement)?.Tag as BinderSlotItem;

        if (e.ClickCount == 2 && item?.Card is { } card)
        {
            _dragPayload = null;
            _viewModel?.SetSelectionSource(() => new List<CollectionCard> { card }, isPlaced: true);
            _viewModel?.OpenCardEditorCommand.Execute(null);
            return;
        }

        _dragStart = e.GetPosition(null);
        _dragPayload = item?.Card is not null
            ? new BinderDragPayload(item.Card.Id, item.Page, item.SlotIndex)
            : null;
    }

    private void SlotCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragPayload is null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        DragDrop.DoDragDrop((DependencyObject)sender, _dragPayload, DragDropEffects.Move);
    }

    private void Slot_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(BinderDragPayload)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Slot_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(BinderDragPayload)) is not BinderDragPayload payload) return;
        if ((sender as FrameworkElement)?.Tag is not BinderSlotItem target) return;
        e.Handled = true;

        // No-op: dropped back on the exact slot it came from.
        if (payload.OriginPage == target.Page && payload.OriginSlot == target.SlotIndex) return;

        _viewModel?.DropOnSlot(payload.LotId, target.Page, target.SlotIndex);
    }

    // Right-clicking a tile selects it (unless it is part of an existing multi-selection), so the
    // shared context menu operates on the clicked card like the main Collection list does.
    private void UnplacedListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        var item = FindAncestor<ListBoxItem>(source);
        if (item?.DataContext is not CollectionCard data) return;

        if (!UnplacedListBox.SelectedItems.Contains(data))
        {
            UnplacedListBox.SelectedItems.Clear();
            UnplacedListBox.SelectedItems.Add(data);
        }

        // The Unplaced pane isn't a binder slot, so "Add Missing Card..." must not appear here.
        _viewModel?.ClearSlotContext();
        _viewModel?.SetSelectionSource(() => UnplacedListBox.SelectedItems.Cast<CollectionCard>().ToList(), isPlaced: false);
    }

    // Attached to both page ItemsControls (not per-slot). ContextMenuOpening (rather than a raw
    // PreviewMouseRightButtonDown) is the event WPF guarantees runs immediately before the menu
    // becomes visible, so there's no ordering ambiguity about whether the selection update below
    // lands before the menu's bindings are evaluated.
    private void SlotGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var item = e.OriginalSource is DependencyObject source ? FindSlotItem(source) : null;

        if (item is null)
        {
            // Right-clicked the page grid but not on an actual slot.
            _viewModel?.ClearSlotContext();
            _viewModel?.ClearSelection();
            return;
        }

        // Record the slot coordinates so "Add Missing Card..." can target this exact page/slot —
        // works for empty slots too, not just occupied ones.
        _viewModel?.SetSlotContext(item.Page, item.SlotIndex);

        if (item.Card is not { } card)
        {
            _viewModel?.ClearSelection();
            return;
        }

        _viewModel?.SetSelectionSource(() => new List<CollectionCard> { card }, isPlaced: true);
    }

    /// <summary>Walks up from the clicked element to find the slot's own BinderSlotItem-tagged
    /// Border — NOT the first Border found, since CardTileTemplate's root is itself a (differently
    /// tagged/untagged) Border nested inside the slot's Border, and a naive "first Border ancestor"
    /// search would stop there instead.</summary>
    private static BinderSlotItem? FindSlotItem(DependencyObject d)
    {
        var current = d;
        while (current is not null)
        {
            if (current is FrameworkElement { Tag: BinderSlotItem item }) return item;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void TagsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        _viewModel.LoadTagFlyoutItems();
        // A Popup auto-closes when its PlacementTarget becomes invisible. The clicked MenuItem
        // goes invisible the instant its owning ContextMenu closes (which happens right after
        // this Click handler runs), so anchor to the always-visible view itself instead.
        TagsPopup.PlacementTarget = this;
        TagsPopup.IsOpen = true;
    }

    private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(d);
        while (parent is not null && parent is not T)
            parent = VisualTreeHelper.GetParent(parent);
        return parent as T;
    }
}
