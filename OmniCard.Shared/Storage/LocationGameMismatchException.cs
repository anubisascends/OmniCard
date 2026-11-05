using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Storage;

/// <summary>Thrown when cards would be added to or moved into a location whose assigned game
/// (<see cref="StorageContainer.Game"/>) differs from the cards' game — a deck box, or any binder/box
/// fixed to one game. Controllers map this to HTTP 409.</summary>
public sealed class LocationGameMismatchException(string locationName, CardGame locationGame, CardGame offendingGame)
    : InvalidOperationException(
        $"\"{locationName}\" holds only {locationGame.DisplayName()} cards and can't take {offendingGame.DisplayName()} cards.")
{
    public string LocationName { get; } = locationName;
    public CardGame LocationGame { get; } = locationGame;
    public CardGame OffendingGame { get; } = offendingGame;
}
