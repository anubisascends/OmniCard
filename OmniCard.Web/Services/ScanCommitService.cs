using OmniCard.Api.Contracts;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Tags;
using OmniCard.Web.Api.Controllers;

namespace OmniCard.Web.Services;

/// <summary>
/// Writes confirmed scans into a storage location as owned lots — shared by the interactive scan commit
/// (<c>POST /api/scan/commit</c>) and watched-folder batch commits, so both apply the deck-box game
/// guard, per-copy tags and matcher learning the same way.
/// </summary>
public sealed class ScanCommitService(
    ICardService cardService,
    WebBinderCardService binderCards,
    ITagService tags,
    ILogger<ScanCommitService> logger)
{
    /// <summary>Create one lot per item in <paramref name="containerId"/>, tag each, and teach the
    /// matcher from the confirmed identities. Returns the created lot ids in input order.</summary>
    /// <exception cref="UnknownScanGameException">An item names an unknown game (nothing is written).</exception>
    /// <exception cref="DeckBoxGameMismatchException">The location is a deck box for another game.</exception>
    public IReadOnlyList<int> Commit(int containerId, IReadOnlyList<ScanCommitItem> items)
    {
        var cards = new List<CollectionCard>(items.Count);
        foreach (var item in items)
            cards.Add(MapScanItem(item, containerId) ?? throw new UnknownScanGameException(item.Game));

        // A scanned card is a real physical copy — always create a new lot (never skip as a duplicate).
        // AddScannedLots returns the created lot ids in input order so we can attach per-copy tags.
        var lotIds = binderCards.AddScannedLots(cards);
        for (var i = 0; i < lotIds.Count; i++)
        {
            var cardTags = items[i].Tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (cardTags.Count > 0)
                tags.SetTagsForLot(lotIds[i], cardTags);
        }

        // Teach the matcher from confirmed identities: record each scanned item's (scan pHash → card)
        // so future scans of the same card auto-match. Best-effort — a failure to record must never
        // fail the commit (the lots are already written).
        RecordScanCorrections(items);
        return lotIds;
    }

    /// <summary>Maps a confirmed scan item to a <see cref="CollectionCard"/> destined for
    /// <paramref name="containerId"/> (null for an export, which places nothing). Returns <c>null</c>
    /// when the item's game is unknown. Shared by the scan-commit, audit-commit and export paths.</summary>
    public static CollectionCard? MapScanItem(ScanCommitItem item, int? containerId)
    {
        if (LocationsController.ParseGame(item.Game) is not { } game)
            return null;

        // Honor an explicit per-item foil finish; fall back to the game's basic foil when foil but
        // no finish was chosen. Non-foil ⇒ no finish.
        var foilType = item.IsFoil
            ? (string.IsNullOrWhiteSpace(item.FoilType) ? FoilTypes.BasicFoilType(game) : item.FoilType.Trim())
            : null;

        return new CollectionCard
        {
            Game = game,
            GameCardId = item.GameCardId,
            Name = item.Name,
            SetCode = item.SetCode,
            SetName = item.SetName,
            Number = item.CollectorNumber,
            Rarity = item.Rarity,
            ImageUri = item.ImageUri,
            Condition = string.IsNullOrWhiteSpace(item.Condition) ? "NM" : item.Condition,
            Language = CardLanguages.Normalize(item.Language) ?? CardLanguages.English,
            IsFoil = item.IsFoil,
            FoilType = foilType,
            Quantity = Math.Max(1, item.Quantity),
            PurchasePrice = item.PurchasePrice,
            Note = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim(),
            DateAdded = DateTime.UtcNow,
            ContainerId = containerId,
        };
    }

    /// <summary>Record a scan-hash → confirmed-card mapping for each committed item that carries a scan
    /// hash, so the matcher recognizes the same card next time (see <see cref="ScanCommitItem.ScanHash"/>).</summary>
    public void RecordScanCorrections(IReadOnlyList<ScanCommitItem> items)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ScanHash) || string.IsNullOrWhiteSpace(item.GameCardId)) continue;
            if (!ulong.TryParse(item.ScanHash, out var hash)) continue;
            if (LocationsController.ParseGame(item.Game) is not { } game) continue;
            try
            {
                cardService.GetGameService(game).RecordCorrection(hash, item.GameCardId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to record scan correction for {Game} card {CardId}", item.Game, item.GameCardId);
            }
        }
    }
}

/// <summary>A scan item named a game this server doesn't know; maps to HTTP 400.</summary>
public sealed class UnknownScanGameException(string game) : Exception($"Unknown game '{game}'");
