using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OmniCard.Models;

namespace OmniCard.Controls;

public partial class CardSearchControl : UserControl
{
    public CardSearchControl()
    {
        InitializeComponent();
    }

    // --- Dependency Properties ---

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(CardSearchControl),
            new PropertyMetadata("Search & Assign Card"));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly DependencyProperty SearchQueryProperty =
        DependencyProperty.Register(nameof(SearchQuery), typeof(string), typeof(CardSearchControl),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string SearchQuery
    {
        get => (string)GetValue(SearchQueryProperty);
        set => SetValue(SearchQueryProperty, value);
    }

    public static readonly DependencyProperty SearchResultsProperty =
        DependencyProperty.Register(nameof(SearchResults), typeof(IEnumerable), typeof(CardSearchControl));

    public IEnumerable SearchResults
    {
        get => (IEnumerable)GetValue(SearchResultsProperty);
        set => SetValue(SearchResultsProperty, value);
    }

    public static readonly DependencyProperty SelectedResultProperty =
        DependencyProperty.Register(nameof(SelectedResult), typeof(CardMatch), typeof(CardSearchControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public CardMatch? SelectedResult
    {
        get => (CardMatch?)GetValue(SelectedResultProperty);
        set => SetValue(SelectedResultProperty, value);
    }

    public static readonly DependencyProperty SearchCommandProperty =
        DependencyProperty.Register(nameof(SearchCommand), typeof(ICommand), typeof(CardSearchControl));

    public ICommand? SearchCommand
    {
        get => (ICommand?)GetValue(SearchCommandProperty);
        set => SetValue(SearchCommandProperty, value);
    }

    public static readonly DependencyProperty AssignCommandProperty =
        DependencyProperty.Register(nameof(AssignCommand), typeof(ICommand), typeof(CardSearchControl));

    public ICommand? AssignCommand
    {
        get => (ICommand?)GetValue(AssignCommandProperty);
        set => SetValue(AssignCommandProperty, value);
    }

    public static readonly DependencyProperty AssignButtonTextProperty =
        DependencyProperty.Register(nameof(AssignButtonText), typeof(string), typeof(CardSearchControl),
            new PropertyMetadata("Assign Selected Card"));

    public string AssignButtonText
    {
        get => (string)GetValue(AssignButtonTextProperty);
        set => SetValue(AssignButtonTextProperty, value);
    }

    public static readonly DependencyProperty ResultsMaxHeightProperty =
        DependencyProperty.Register(nameof(ResultsMaxHeight), typeof(double), typeof(CardSearchControl),
            new PropertyMetadata(250.0));

    public double ResultsMaxHeight
    {
        get => (double)GetValue(ResultsMaxHeightProperty);
        set => SetValue(ResultsMaxHeightProperty, value);
    }

    // --- Event Handlers ---

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SearchCommand?.CanExecute(null) == true)
        {
            SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SearchResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AssignCommand?.CanExecute(null) == true)
            AssignCommand.Execute(null);
    }

    public void FocusSearchBox()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}
