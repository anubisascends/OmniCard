using OmniCard.Data;
using OmniCard.Shared.Cards;

namespace OmniCard.Collection.Inventory;

/// <summary>The single enforcement point for the one-game location rule. Every card-in path (web
/// move, scan commit, imports, desktop moves/adds, deck-box sync) funnels through here so the hard block
/// can't drift between the card services. A location with an assigned
/// <see cref="Shared.Storage.StorageContainer.Game"/> — a deck box, or any binder/box fixed to a game —
/// rejects cards from another game; a location without a game allows anything. A location's default
/// sets are only a search default and are never enforced here.</summary>
public static class LocationGameGuard
{
    /// <summary>Validates that every game in <paramref name="incomingGames"/> is allowed into the
    /// target location. Throws <see cref="Shared.Storage.LocationGameMismatchException"/> on the first
    /// mismatch. A null <paramref name="targetContainerId"/> (unplaced) is always allowed.</summary>
    public static void ValidateIncoming(OmniCardDbContext context, int? targetContainerId,
        IEnumerable<CardGame> incomingGames)
    {
        if (targetContainerId is not int containerId)
            return;

        var target = context.StorageContainers
            .Where(c => c.Id == containerId)
            .Select(c => new { c.Name, c.Game })
            .FirstOrDefault();

        // No game fixed → nothing to enforce.
        if (target?.Game is not CardGame locationGame)
            return;

        foreach (var game in incomingGames)
        {
            if (game != locationGame)
                throw new Shared.Storage.LocationGameMismatchException(target.Name, locationGame, game);
        }
    }
}
