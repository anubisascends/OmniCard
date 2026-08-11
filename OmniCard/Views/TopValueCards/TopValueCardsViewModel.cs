using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.TopValueCards;

/// <summary>Backs the Top 100 Cards dialog: the highest market-value unlisted singles across
/// every game, with a jump-to-location action per row. Loads once on open via <see cref="Load"/>.</summary>
public sealed partial class TopValueCardsViewModel(ICollectionQueryService queryService) : ViewModel
{
    private const int Take = 100;

    public ObservableCollection<CollectionCard> Cards { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public Action? CloseDialog { get; set; }

    /// <summary>Set when the user picks "Go to Location" on a row; read by the caller after the
    /// dialog closes.</summary>
    public (CardGame Game, int? ContainerId)? NavigationResult { get; private set; }

    public void Load()
    {
        IsBusy = true;
        try
        {
            Cards.Clear();
            foreach (var card in queryService.GetTopValueCards(Take))
                Cards.Add(card);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void GoToLocation(CollectionCard card)
    {
        NavigationResult = (card.Game, card.ContainerId);
        CloseDialog?.Invoke();
    }
}
