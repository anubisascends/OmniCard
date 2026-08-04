using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.PickTrade;

public sealed partial class PickTradeViewModel(ITradeService tradeService) : ViewModel
{
    public ObservableCollection<TradeSummary> Trades { get; } = [];

    [ObservableProperty]
    public partial TradeSummary? SelectedTrade { get; set; }

    public Action<bool>? CloseDialog { get; set; }

    public TradeSummary? Result { get; private set; }

    public void Load()
    {
        Trades.Clear();
        foreach (var trade in tradeService.GetTrades())
            Trades.Add(trade);
    }

    [RelayCommand]
    public void Confirm()
    {
        if (SelectedTrade is null) return;
        Result = SelectedTrade;
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}
