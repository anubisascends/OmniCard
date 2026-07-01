using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Models;

namespace OmniCard.Views.Card;

public sealed partial class CardViewModel : ViewModel
{
    [ObservableProperty]
    public partial ScannedCard Card { get; set; }

    [ObservableProperty]
    public partial double Zoom { get; set; } = 1.0;
}
