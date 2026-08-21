using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Trades;

/// <summary>Read-only history of every trade session — outgoing cards (owned + off-database),
/// received note/photo, value delta, and fulfillment status. Populated from
/// <see cref="ITradeService.GetTrades"/>.</summary>
public sealed partial class TradesViewModel(ITradeService tradeService) : ViewModel
{
    public ObservableCollection<TradeSummary> Trades { get; } = [];

    [ObservableProperty]
    public partial TradeSummary? SelectedTrade { get; set; }

    public Action? CloseDialog { get; set; }

    public void Load()
    {
        Trades.Clear();
        foreach (var trade in tradeService.GetTrades())
            Trades.Add(trade);
        SelectedTrade = Trades.FirstOrDefault();
    }

    [RelayCommand]
    public void Close() => CloseDialog?.Invoke();
}
