using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Storage;

namespace OmniCard.Collection.Lists;

/// <summary>How much of one list item the collection already covers: the exact copies to pull
/// (<see cref="Picks"/>) and how many still have to be bought.</summary>
public sealed record ListItemPlan(CardListItem Item, IReadOnlyList<DecklistPick> Picks)
{
    public int OwnedQuantity => Picks.Sum(p => p.Quantity);
    public int MissingQuantity => Math.Max(0, Item.Quantity - OwnedQuantity);
}

/// <summary>Splits a saved list into owned copies (to pick and move) and missing copies (to buy and add
/// as new lots). It drives the pick/buy-list prints, the per-row owned counts, and list fulfillment.</summary>
public sealed class ListFulfillmentPlanner(IDbContextFactory<OmniCardDbContext> dbContextFactory)
{
    /// <summary>Allocates owned copies to each item by <em>exact printing</em> (same game card id and
    /// foil flag). The lot an item was added from is claimed first, whatever its finish or location.
    /// Copies that are listed for sale, flagged missing, traded, or kept in a location excluded from deck
    /// checks are left alone. Items <see cref="CardListItem.AwaitingPurchase"/> get no picks.
    ///
    /// <para>Allocation is shared across the list so two items can't claim the same physical copy. Copies
    /// already in <paramref name="preferContainerId"/> are taken first (they need no move), then copies
    /// not sleeved in a deck box, then in location/section/page/slot order so the pick list walks each
    /// location in order. <paramref name="siteIds"/> limits the search to those sites (null = all).</para></summary>
    public IReadOnlyList<ListItemPlan> Plan(CardGame game, IReadOnlyList<CardListItem> items,
        IReadOnlyCollection<int>? siteIds = null, int? preferContainerId = null)
    {
        var matchable = items.Where(i => !i.AwaitingPurchase && !string.IsNullOrEmpty(i.GameCardId)).ToList();
        if (matchable.Count == 0)
            return items.Select(i => new ListItemPlan(i, [])).ToList();

        var cardIds = matchable.Select(i => i.GameCardId).Distinct().ToList();
        var sourceLotIds = matchable.Where(i => i.SourceLotId is not null).Select(i => i.SourceLotId!.Value).Distinct().ToList();
        var siteList = siteIds?.Distinct().ToList();
        var defaultSiteVisible = siteList?.Contains(Shared.Sites.Site.DefaultSiteId) ?? true;

        using var ctx = dbContextFactory.CreateDbContext();
        var candidates =
            (from l in ctx.Lots.AsNoTracking()
             join p in ctx.Products.AsNoTracking() on l.ProductId equals p.Id
             where p.Category == ProductCategory.Single && p.Game == game
             where (p.GameCardId != null && cardIds.Contains(p.GameCardId)) || sourceLotIds.Contains(l.Id)
             where !l.IsMissing && !l.IsTraded
             join sc in ctx.StorageContainers.AsNoTracking() on l.LocationId equals sc.Id into containerJoin
             from sc in containerJoin.DefaultIfEmpty()
             where siteList == null || (sc == null ? defaultSiteVisible : siteList.Contains(sc.SiteId))
             select new { Lot = l, Product = p, Container = sc })
            .ToList();

        var listedLotIds = ctx.Listings.AsNoTracking()
            .Where(l => l.Status == ListingStatus.Listed || l.Status == ListingStatus.Picked)
            .Select(l => l.LotId)
            .ToHashSet();
        candidates = candidates.Where(c => !listedLotIds.Contains(c.Lot.Id)).ToList();

        var remaining = candidates.ToDictionary(c => c.Lot.Id, c => Math.Max(c.Lot.Quantity, 1));
        var picksByItem = new Dictionary<int, List<DecklistPick>>();

        // Items pointing at a specific owned lot claim it before same-printing items can take it.
        foreach (var item in matchable.OrderBy(i => i.SourceLotId is null))
        {
            var ordered = candidates
                .Where(c => c.Lot.Id == item.SourceLotId
                    || (c.Product.GameCardId == item.GameCardId && c.Product.Foil == item.IsFoil
                        && c.Container?.ExcludeFromDeckCheck != true))
                .OrderByDescending(c => c.Lot.Id == item.SourceLotId)
                .ThenByDescending(c => preferContainerId is not null && c.Lot.LocationId == preferContainerId)
                .ThenBy(c => c.Container?.ContainerType == ContainerType.DeckBox)
                .ThenBy(c => c.Container?.Name ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Lot.Section ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Lot.Page ?? int.MaxValue)
                .ThenBy(c => c.Lot.Slot ?? int.MaxValue);

            var picks = new List<DecklistPick>();
            var needed = item.Quantity;
            foreach (var c in ordered)
            {
                if (needed <= 0) break;
                var available = remaining.GetValueOrDefault(c.Lot.Id);
                if (available <= 0) continue;

                var take = Math.Min(available, needed);
                remaining[c.Lot.Id] = available - take;
                needed -= take;
                picks.Add(new DecklistPick(
                    LotId: c.Lot.Id,
                    ContainerId: c.Lot.LocationId,
                    ContainerName: c.Container?.Name ?? "Unknown",
                    ContainerType: c.Container?.ContainerType,
                    Page: c.Lot.Page,
                    Slot: c.Lot.Slot,
                    Section: c.Lot.Section,
                    SetCode: c.Product.SetCode ?? "",
                    CollectorNumber: c.Product.CollectorNumber ?? "",
                    IsFoil: c.Product.Foil,
                    Condition: c.Lot.Condition ?? "",
                    Quantity: take,
                    IsListed: false));
            }
            picksByItem[item.Id] = picks;
        }

        return items.Select(i => new ListItemPlan(i, picksByItem.GetValueOrDefault(i.Id) ?? [])).ToList();
    }

    /// <summary>Shapes a plan as a <see cref="DecklistCheckResult"/> so the decklist pick/missing PDFs
    /// can print it: owned copies become pull entries with their picks, the shortfall becomes missing
    /// entries priced at the list's captured market price.</summary>
    public static DecklistCheckResult ToCheckResult(string listName, IReadOnlyList<ListItemPlan> plan) => new()
    {
        DeckName = listName,
        DeckSource = "list",
        OwnedEntries = plan
            .Where(p => p.OwnedQuantity > 0)
            .Select(p => new OwnedDecklistEntry(p.Item.CardName, p.Item.SetCode, p.Item.CollectorNumber,
                p.OwnedQuantity, [], Picks: p.Picks.ToList()))
            .OrderBy(e => e.CardName, StringComparer.OrdinalIgnoreCase)
            .ToList(),
        MissingEntries = plan
            .Where(p => p.MissingQuantity > 0)
            .Select(p => new MissingDecklistEntry(
                p.Item.IsFoil ? $"{p.Item.CardName}  ✦" : p.Item.CardName,
                p.Item.SetCode, p.Item.CollectorNumber, p.MissingQuantity,
                p.Item.IsUnpriced ? null : p.Item.AddedMarketPrice))
            .ToList(),
    };
}
