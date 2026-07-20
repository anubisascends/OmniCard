using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Models;

namespace OmniCard.Views.SalesListing;

public partial class ListForSaleViewModel : ObservableObject
{
    public ListForSaleViewModel(decimal suggestedPrice)
    {
        Price = suggestedPrice;
    }

    public SalesChannel[] Channels { get; } = [SalesChannel.TcgPlayer, SalesChannel.Manual];

    [ObservableProperty]
    public partial SalesChannel SelectedChannel { get; set; } = SalesChannel.TcgPlayer;

    [ObservableProperty]
    public partial decimal Price { get; set; }

    [ObservableProperty]
    public partial int Quantity { get; set; } = 1;

    public ListForSaleResult ToResult() => new(SelectedChannel, Price, Quantity);
}
