using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OmniCard.Controls;

/// <summary>Type-to-add tag chip editor: current tags render as removable chips, typing shows
/// autocomplete suggestions from <see cref="AllTagSuggestions"/>, Enter (or clicking a
/// suggestion) adds. Reused by the scanner review panel, the collection card editor, and
/// anywhere else a card's tag set needs editing.</summary>
public partial class TagEditor : UserControl
{
    public TagEditor()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty TagsProperty =
        DependencyProperty.Register(nameof(Tags), typeof(ObservableCollection<string>), typeof(TagEditor));

    public ObservableCollection<string> Tags
    {
        get => (ObservableCollection<string>)GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }

    public static readonly DependencyProperty AllTagSuggestionsProperty =
        DependencyProperty.Register(nameof(AllTagSuggestions), typeof(IEnumerable<string>), typeof(TagEditor),
            new PropertyMetadata(Array.Empty<string>()));

    public IEnumerable<string> AllTagSuggestions
    {
        get => (IEnumerable<string>)GetValue(AllTagSuggestionsProperty);
        set => SetValue(AllTagSuggestionsProperty, value);
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateSuggestions();

    private void UpdateSuggestions()
    {
        var text = InputBox.Text;
        if (string.IsNullOrWhiteSpace(text) || Tags is null)
        {
            SuggestionsPopup.IsOpen = false;
            return;
        }

        var matches = AllTagSuggestions
            .Where(t => t.Contains(text, StringComparison.OrdinalIgnoreCase)
                        && !Tags.Contains(t, StringComparer.OrdinalIgnoreCase))
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        SuggestionsList.ItemsSource = matches;
        SuggestionsPopup.IsOpen = matches.Count > 0;
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddTag(InputBox.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SuggestionsPopup.IsOpen = false;
        }
        else if (e.Key == Key.Down && SuggestionsPopup.IsOpen && SuggestionsList.Items.Count > 0)
        {
            SuggestionsList.Focus();
            SuggestionsList.SelectedIndex = 0;
            e.Handled = true;
        }
    }

    private void SuggestionsList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (SuggestionsList.SelectedItem is string tag)
            AddTag(tag);
    }

    private void SuggestionsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SuggestionsList.SelectedItem is string tag)
        {
            AddTag(tag);
            e.Handled = true;
        }
    }

    private void AddTag(string? raw)
    {
        var name = raw?.Trim();
        if (string.IsNullOrEmpty(name) || Tags is null) return;

        if (!Tags.Contains(name, StringComparer.OrdinalIgnoreCase))
            Tags.Add(name);

        InputBox.Text = "";
        SuggestionsPopup.IsOpen = false;
        InputBox.Focus();
    }

    private void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tagName })
            Tags?.Remove(tagName);
    }
}
