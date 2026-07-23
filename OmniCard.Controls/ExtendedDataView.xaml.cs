using System.Windows;
using System.Windows.Controls;
using OmniCard.Models;

namespace OmniCard.Controls;

/// <summary>
/// Renders a TcgCsvCard's ExtendedDataJson (TCGCSV per-game attributes, e.g. Element, Cost,
/// Power, HP, ATK/DEF) as a labeled name/value list. Parsing itself lives in the plain,
/// non-UI ExtendedDataParser (OmniCard.Shared) so it can be reused outside WPF (e.g. web).
/// </summary>
public partial class ExtendedDataView : UserControl
{
    public ExtendedDataView() => InitializeComponent();

    public static readonly DependencyProperty JsonProperty =
        DependencyProperty.Register(nameof(Json), typeof(string), typeof(ExtendedDataView),
            new PropertyMetadata(null, OnJsonChanged));

    public string? Json
    {
        get => (string?)GetValue(JsonProperty);
        set => SetValue(JsonProperty, value);
    }

    private static void OnJsonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ExtendedDataView)d).Items.ItemsSource = ExtendedDataParser.Parse(e.NewValue as string);
}
