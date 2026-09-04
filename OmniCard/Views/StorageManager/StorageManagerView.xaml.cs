using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OmniCard.Helpers;

namespace OmniCard.Views.StorageManager;

public partial class StorageManagerView : Window, IView<StorageManagerViewModel>
{
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

    // Guards against re-entrancy: a failed rename shows a MessageBox, which steals focus from the
    // edit box and fires LostFocus mid-commit — without this that would loop the dialog forever.
    private bool _committingRename;

    private void DeleteLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ContainerDisplayItem item)
            return;

        if (DeleteLocationPrompt.Confirm(this, item.Name, out var moveToBulk))
            ViewModel.DeleteLocation(item.Id, moveToBulk);
    }

    private void GroupHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is LocationGroupViewModel group)
            group.IsExpanded = !group.IsExpanded;
    }

    // --- Inline rename (double-click a location name) ---

    private void LocationName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not ContainerDisplayItem item)
            return;

        // Only one row is ever editable at a time: close any other open editor first (discarding
        // its unsaved text), then open this one.
        CancelAllEditing(except: item);
        item.EditName = item.Name;
        item.IsEditing = true;
        e.Handled = true;
    }

    private void CancelAllEditing(ContainerDisplayItem? except = null)
    {
        foreach (var group in ViewModel.Groups)
            foreach (var row in group.Items)
                if (!ReferenceEquals(row, except))
                    row.IsEditing = false;
    }

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // The edit box is always in the tree (just collapsed), so Loaded only fires once while it's
        // hidden. Focus it each time it actually becomes visible, deferred so focus lands after the
        // layout pass that shows it.
        if (sender is TextBox box && box.IsVisible)
        {
            box.Dispatcher.BeginInvoke(new Action(() =>
            {
                box.Focus();
                Keyboard.Focus(box);
                box.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not ContainerDisplayItem item)
            return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitRename(item, box);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            item.IsEditing = false; // cancel — LostFocus sees IsEditing false and no-ops
        }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not ContainerDisplayItem item)
            return;
        if (item.IsEditing)
            CommitRename(item, box);
    }

    private void CommitRename(ContainerDisplayItem item, TextBox box)
    {
        if (_committingRename) return;
        _committingRename = true;
        try
        {
            var newName = (item.EditName ?? "").Trim();

            // No change (ignoring whitespace) — just close the editor.
            if (string.Equals(newName, item.Name, StringComparison.Ordinal))
            {
                item.IsEditing = false;
                return;
            }

            if (ViewModel.TryRename(item.Id, newName, out var error))
            {
                item.IsEditing = false; // Load() rebuilds the row anyway
            }
            else
            {
                System.Windows.MessageBox.Show(this, error, "Rename Location",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                // Keep editing so the user can fix it; re-focus.
                box.Focus();
                box.SelectAll();
            }
        }
        finally
        {
            _committingRename = false;
        }
    }
}
