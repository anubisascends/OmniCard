using OmniCard.Api.Contracts;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.CardMatching;
using OmniCard.Shared.Games;
using OmniCard.Shared.Tags;
using OmniCard.Web.Api.Controllers;
using OmniCard.Web.Services.BuyList;

namespace OmniCard.Web.Services;

/// <summary>Lots from a commit that went through the Buy List, and what it did with them.</summary>
public sealed record ScanCommitOutcome(IReadOnlyList<int> LotIds, BuyListCommitResultDto? BuyList);

/// <summary>
/// Writes confirmed scans into a storage location as owned lots — shared by the interactive scan commit
/// (<c>POST /api/scan/commit</c>) and watched-folder batch commits, so both apply the deck-box game
/// guard, per-copy tags and matcher learning the same way.
/// </summary>
public sealed class ScanCommitService(
    ICardService cardService,
    WebBinderCardService binderCards,
    ITagService tags,
    BuyListService buyList,
    ILogger<ScanCommitService> logger)
{
    /// <summary><see cref="Commit"/>, plus the Buy List: copies still wanted count down and go to the
    /// game's set-aside location, a stack splitting when only some of it is wanted. A copy stays in
    /// <paramref name="containerId"/> when <paramref name="canWriteLocation"/> refuses the set-aside
    /// location (null allows everything).</summary>
    /// <exception cref="UnknownScanGameException">An item names an unknown game (nothing is written).</exception>
    /// <exception cref="DeckBoxGameMismatchException">The location is a deck box for another game.</exception>
    public ScanCommitOutcome CommitWithBuyList(int containerId, IReadOnlyList<ScanCommitItem> items, Func<int, bool>? canWriteLocation = null)
    {
        var candidates = items
            .Select(i => new BuyListCandidate(LocationsController.ParseGame(i.Game) ?? throw new UnknownScanGameException(i.Game),
                i.Name, i.GameCardId, i.IsFoil, Math.Max(1, i.Quantity)))
            .ToList();
        var allocations = buyList.Allocate(candidates);
        var setAside = candidates.Select(c => c.Game).Distinct().ToDictionary(g => g, buyList.SetAsideLocation);

        var stay = new List<ScanCommitItem>(items.Count);
        var moved = new Dictionary<int, List<ScanCommitItem>>();
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int setAsideCopies = 0, notSetAside = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var copies = allocations[i].Copies;
            var target = copies > 0 ? setAside[candidates[i].Game] : null;
            if (target is not { } location || location.Id == containerId)
            {
                stay.Add(items[i]);
                continue;
            }
            if (canWriteLocation?.Invoke(location.Id) == false)
            {
                stay.Add(items[i]);
                notSetAside += copies;
                continue;
            }

            if (!moved.TryGetValue(location.Id, out var group)) moved[location.Id] = group = [];
            group.Add(items[i] with { Quantity = copies });
            if (candidates[i].Quantity > copies) stay.Add(items[i] with { Quantity = candidates[i].Quantity - copies });
            names.Add(location.Name);
            setAsideCopies += copies;
        }

        // The chosen location first: it is the one a mismatch can refuse, and nothing has been written yet.
        var lotIds = new List<int>(items.Count);
        if (stay.Count > 0) lotIds.AddRange(Commit(containerId, stay));
        foreach (var (locationId, group) in moved) lotIds.AddRange(Commit(locationId, group));

        buyList.Consume(allocations);
        var matched = allocations.Sum(a => a.Copies);
        return new ScanCommitOutcome(lotIds, matched == 0 && notSetAside == 0
            ? null
            : new BuyListCommitResultDto(matched, setAsideCopies, names.ToList(), notSetAside));
    }

    /// <summary>Create one lot per item in <paramref name="containerId"/>, tag each, and teach the
    /// matcher from the confirmed identities. Returns the created lot ids in input order.</summary>
    /// <exception cref="UnknownScanGameException">An item names an unknown game (nothing is written).</exception>
    /// <exception cref="DeckBoxGameMismatchException">The location is a deck box for another game.</exception>
    public IReadOnlyList<int> Commit(int containerId, IReadOnlyList<ScanCommitItem> items)
    {
        var cards = new List<CollectionCard>(items.Count);
        // One catalog lookup per printing, however many copies of it the commit holds.
        var attributes = new Dictionary<(CardGame, string), (string? Color, string? CardType)>();
        foreach (var item in items)
        {
            var card = MapScanItem(item, containerId) ?? throw new UnknownScanGameException(item.Game);
            if (attributes.TryGetValue((card.Game, card.GameCardId), out var known))
                (card.Color, card.CardType) = known;
            else
            {
                FillCatalogAttributes(card);
                attributes[(card.Game, card.GameCardId)] = (card.Color, card.CardType);
            }
            cards.Add(card);
        }

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

    /// <summary>Fills the card's color and full type line from its catalog printing (the scan payload
    /// doesn't carry them). Without these, owned-collection <c>c:</c>/<c>t:</c> searches — and tag rules
    /// built on them — can't see scanned cards. Best-effort: a missing catalog row leaves them empty.</summary>
    public void FillCatalogAttributes(CollectionCard card)
    {
        try
        {
            if (cardService.GetGameService(card.Game) is { } gameService)
                CardAttributeExtractor.FillFromCatalog(card, gameService);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to look up catalog attributes for {Game} card {CardId}", card.Game, card.GameCardId);
        }
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
