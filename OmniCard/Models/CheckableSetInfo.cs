using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniCard.Models;

public partial class CheckableSetInfo : ObservableObject
{
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";
    public string DisplayName => $"{SetName} ({SetCode})";

    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}
