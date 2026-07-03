using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniCard.Models;

public partial class SetFilterItem : ObservableObject
{
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";
    public string DisplayName => $"{SetName} ({SetCode})";

    [ObservableProperty]
    public partial DrawingImage? Symbol { get; set; }
}
