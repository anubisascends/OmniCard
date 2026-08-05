using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OmniCard.Controls;

/// <summary>Filterable, checkable tag picker hosted in a Popup from a "Tags..." context/main
/// menu item. Toggling a row fires <see cref="ToggleCommand"/> with (name, applying); the "+ New
/// Tag..." row fires <see cref="NewTagCommand"/> with the trimmed name. The bound
/// <see cref="Tags"/> collection is expected to already reflect the current selection's
/// checked/unchecked/indeterminate state — the host recomputes and reassigns it before opening
/// the popup on each use. Filtering re-derives <c>TagsList.ItemsSource</c> as a plain
/// <see cref="List{T}"/> snapshot on every keystroke (same pattern as <see cref="TagEditor"/>'s
/// suggestion list) rather than a live <c>CollectionViewSource</c>, since a
/// <c>CollectionViewSource</c>'s <c>View</c> reference is not guaranteed stable across a
/// <c>Source</c> reassignment.</summary>
public partial class TagFlyout : UserControl
{
    public TagFlyout()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty TagsProperty =
        DependencyProperty.Register(nameof(Tags), typeof(ObservableCollection<TagFlyoutItem>),
            typeof(TagFlyout), new PropertyMetadata(null, OnTagsChanged));

    public ObservableCollection<TagFlyoutItem> Tags
    {
        get => (ObservableCollection<TagFlyoutItem>)GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }

    private static void OnTagsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TagFlyout)d;
        control.FilterBox.Text = "";
        control.NewTagBox.Text = "";
        control.NewTagBox.Visibility = Visibility.Collapsed;
        control.NewTagLabel.Visibility = Visibility.Visible;
        control.RefreshFilteredList();
    }

    public static readonly DependencyProperty ToggleCommandProperty =
        DependencyProperty.Register(nameof(ToggleCommand), typeof(ICommand), typeof(TagFlyout));

    public ICommand? ToggleCommand
    {
        get => (ICommand?)GetValue(ToggleCommandProperty);
        set => SetValue(ToggleCommandProperty, value);
    }

    public static readonly DependencyProperty NewTagCommandProperty =
        DependencyProperty.Register(nameof(NewTagCommand), typeof(ICommand), typeof(TagFlyout));

    public ICommand? NewTagCommand
    {
        get => (ICommand?)GetValue(NewTagCommandProperty);
        set => SetValue(NewTagCommandProperty, value);
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshFilteredList();

    private void RefreshFilteredList()
    {
        if (Tags is null)
        {
            TagsList.ItemsSource = null;
            return;
        }

        var text = FilterBox.Text;
        TagsList.ItemsSource = string.IsNullOrWhiteSpace(text)
            ? Tags.ToList()
            : Tags.Where(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void TagsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not TagFlyoutItem item) return;

        var applying = item.State != TagCheckState.Checked; // Unchecked or Indeterminate -> apply; Checked -> remove
        item.State = applying ? TagCheckState.Checked : TagCheckState.Unchecked; // optimistic UI update
        ToggleCommand?.Execute((item.Name, applying));
    }

    private void NewTagLabel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        NewTagLabel.Visibility = Visibility.Collapsed;
        NewTagBox.Visibility = Visibility.Visible;
        NewTagBox.Focus();
    }

    private void NewTagBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitNewTag();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelNewTag();
            e.Handled = true;
        }
    }

    private void NewTagBox_LostFocus(object sender, RoutedEventArgs e) => CancelNewTag();

    private void CommitNewTag()
    {
        var name = NewTagBox.Text.Trim();
        if (name.Length > 0)
            NewTagCommand?.Execute(name);
        CancelNewTag();
    }

    private void CancelNewTag()
    {
        NewTagBox.Text = "";
        NewTagBox.Visibility = Visibility.Collapsed;
        NewTagLabel.Visibility = Visibility.Visible;
    }
}
