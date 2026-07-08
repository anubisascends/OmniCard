using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Models;

namespace OmniCard.Views.SetFilterBuilder;

/// <summary>
/// WPF-specific extension of SetFilterItem that adds an observable DrawingImage Symbol property
/// for displaying set symbols in the SetFilterBuilder UI.
/// </summary>
public partial class WpfSetFilterItem : ObservableObject
{
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";
    public string DisplayName => $"{SetName} ({SetCode})";

    [ObservableProperty]
    public partial DrawingImage? Symbol { get; set; }
}
