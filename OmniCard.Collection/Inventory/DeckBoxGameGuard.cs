using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Storage;

namespace OmniCard.Collection.Inventory;

/// <summary>The single enforcement point for the deck-box single-game rule. Every card-in path (web
/// move, scan commit, desktop moves/adds, deck-box sync) funnels through here so the hard block can't
/// drift between the desktop and web card services. A deck box with an assigned game rejects any card
/// from another game; deck boxes without a game, and all other container types, allow anything.</summary>
public static class DeckBoxGameGuard
{
    /// <summary>Validates that every game in <paramref name="incomingGames"/> is allowed into the
    /// target container. Throws <see cref="DeckBoxGameMismatchException"/> on the first mismatch. A
    /// null <paramref name="targetContainerId"/> (unplaced) is always allowed.</summary>
    public static void ValidateIncoming(OmniCardDbContext context, int? targetContainerId,
        IEnumerable<CardGame> incomingGames)
    {
        if (targetContainerId is not int containerId)
            return;

        var target = context.StorageContainers
            .Where(c => c.Id == containerId)
            .Select(c => new { c.Name, c.ContainerType, c.Game })
            .FirstOrDefault();

        // Not a game-locked deck box → nothing to enforce.
        if (target is null || target.ContainerType != ContainerType.DeckBox || target.Game is not CardGame deckGame)
            return;

        foreach (var game in incomingGames)
        {
            if (game != deckGame)
                throw new DeckBoxGameMismatchException(target.Name, deckGame, game);
        }
    }
}
