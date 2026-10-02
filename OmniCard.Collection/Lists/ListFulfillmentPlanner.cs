using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
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

/// <summary>An owned lot of a <em>different</em> printing (same card name) that could stand in for a list
/// item the collection doesn't have. <see cref="Available"/> is what's left after the exact-printing plan
/// took its copies; <see cref="Suggested"/> is the pre-filled amount, shared across items so two items
/// aren't both offered the same copies.</summary>
public sealed record SubstituteCandidate(
    int LotId,
    string GameCardId,
    string CardName,
    string? SetCode,
    string? CollectorNumber,
    bool IsFoil,
    string Language,
    string Condition,
    string ContainerName,
    int? Page,
    int? Slot,
    string? Section,
    int Available,
    int Suggested);

/// <summary>A list item the exact-printing match left short, with the owned stand-ins found for it.</summary>
public sealed record ItemSubstitutes(CardListItem Item, int MissingQuantity, IReadOnlyList<SubstituteCandidate> Candidates);

/// <summary>Splits a saved list into owned copies (to pick and move) and missing copies (to buy and add
/// as new lots). It drives the pick/buy-list prints, the per-row owned counts, list fulfillment, and the
/// "find in collection" stand-in search.</summary>
public sealed class ListFulfillmentPlanner(IDbContextFactory<OmniCardDbContext> dbContextFactory)
{
    private sealed record Candidate(InventoryLot Lot, Product Product, StorageContainer? Container, string Language, string PrintingKey);

    /// <summary>Allocates owned copies to each item by <em>exact printing</em>: the same card id, or the same
    /// printing in another language (same <see cref="PrintingIdentity"/> key and card name), with the same
    /// foil flag. A forced
    /// <paramref name="language"/> (null = any) only counts copies in that language. The lot an item was
    /// added from is claimed first, whatever its finish, language or location. Copies that are listed for
    /// sale, flagged missing, traded, or kept in a location excluded from deck checks are left alone.
    /// Items <see cref="CardListItem.AwaitingPurchase"/> get no picks.
    ///
    /// <para>Allocation is shared across the list so two items can't claim the same physical copy. Copies
    /// already in <paramref name="preferContainerId"/> are taken first (they need no move), then copies
    /// not sleeved in a deck box, then in location/section/page/slot order so the pick list walks each
    /// location in order. <paramref name="siteIds"/> limits the search to those sites (null = all).</para></summary>
    public IReadOnlyList<ListItemPlan> Plan(CardGame game, IReadOnlyList<CardListItem> items,
        IReadOnlyCollection<int>? siteIds = null, int? preferContainerId = null, string? language = null)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var candidates = LoadCandidates(ctx, game, items, siteIds);
        return PlanCore(game, items, candidates, preferContainerId, CardLanguages.Normalize(language)).Plans;
    }

    /// <summary>For every item the exact-printing plan leaves short, the owned copies of <em>other</em>
    /// printings of the same card name (matched case-insensitively; Riftbound's ", " vs " - " spelling is
    /// tried too) that are still free after the plan. Same rules as <see cref="Plan"/>: forced language,
    /// readable sites, no listed/missing/traded copies or deck-check-excluded locations. Candidates come
    /// same set first, then same finish, then outside deck boxes, then by location.</summary>
    public IReadOnlyList<ItemSubstitutes> FindSubstitutes(CardGame game, IReadOnlyList<CardListItem> items,
        IReadOnlyCollection<int>? siteIds = null, string? language = null)
    {
        language = CardLanguages.Normalize(language);
        using var ctx = dbContextFactory.CreateDbContext();
        var candidates = LoadCandidates(ctx, game, items, siteIds);
        var (plans, remaining) = PlanCore(game, items, candidates, preferContainerId: null, language);
        var suggestLeft = new Dictionary<int, int>(remaining);

        var result = new List<ItemSubstitutes>();
        foreach (var plan in plans.Where(p => p.MissingQuantity > 0))
        {
            var item = plan.Item;
            var names = NameVariants(item.CardName);
            var ordered = candidates
                .Where(c => names.Contains(c.Product.Name, StringComparer.OrdinalIgnoreCase)
                    && remaining.GetValueOrDefault(c.Lot.Id) > 0
                    && c.Container?.ExcludeFromDeckCheck != true
                    && (language is null || c.Language == language))
                .OrderByDescending(c => string.Equals(c.Product.SetCode, item.SetCode, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(c => c.Product.Foil == item.IsFoil)
                .ThenBy(c => c.Container?.ContainerType == ContainerType.DeckBox)
                .ThenBy(c => c.Container?.Name ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Lot.Section ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Lot.Page ?? int.MaxValue)
                .ThenBy(c => c.Lot.Slot ?? int.MaxValue);

            var needed = plan.MissingQuantity;
            var found = new List<SubstituteCandidate>();
            foreach (var c in ordered)
            {
                var suggest = Math.Min(needed, suggestLeft.GetValueOrDefault(c.Lot.Id));
                if (suggest > 0)
                {
                    suggestLeft[c.Lot.Id] -= suggest;
                    needed -= suggest;
                }
                found.Add(new SubstituteCandidate(
                    c.Lot.Id, c.Product.GameCardId ?? "", c.Product.Name, c.Product.SetCode, c.Product.CollectorNumber,
                    c.Product.Foil, c.Language, c.Lot.Condition ?? "", c.Container?.Name ?? "Unknown",
                    c.Lot.Page, c.Lot.Slot, c.Lot.Section, remaining[c.Lot.Id], suggest));
            }
            result.Add(new ItemSubstitutes(item, plan.MissingQuantity, found));
        }
        return result;
    }

    private static (IReadOnlyList<ListItemPlan> Plans, Dictionary<int, int> Remaining) PlanCore(
        CardGame game, IReadOnlyList<CardListItem> items, List<Candidate> candidates, int? preferContainerId, string? language)
    {
        var remaining = candidates.ToDictionary(c => c.Lot.Id, c => Math.Max(c.Lot.Quantity, 1));
        var picksByItem = new Dictionary<int, List<DecklistPick>>();
        var matchable = items.Where(i => !i.AwaitingPurchase && !string.IsNullOrEmpty(i.GameCardId));

        // Items pointing at a specific owned lot claim it before same-printing items can take it.
        foreach (var item in matchable.OrderBy(i => i.SourceLotId is null))
        {
            var key = PrintingIdentity.Key(game, item.GameCardId, item.SetCode, item.CollectorNumber);
            var ordered = candidates
                .Where(c => c.Lot.Id == item.SourceLotId
                    || ((c.Product.GameCardId == item.GameCardId
                         || (c.PrintingKey == key && string.Equals(c.Product.Name, item.CardName, StringComparison.OrdinalIgnoreCase)))
                        && c.Product.Foil == item.IsFoil
                        && c.Container?.ExcludeFromDeckCheck != true
                        && (language is null || c.Language == language)))
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

        var plans = items.Select(i => new ListItemPlan(i, picksByItem.GetValueOrDefault(i.Id) ?? [])).ToList();
        return (plans, remaining);
    }

    /// <summary>Every owned single the list could draw on: copies of the items' cards by id or by name
    /// (other-language rows of a printing carry the English name, and stand-ins are found by name), plus the
    /// lots items were added from. Lots outside <paramref name="siteIds"/>, flagged missing, traded, or listed
    /// for sale are dropped.</summary>
    private static List<Candidate> LoadCandidates(OmniCardDbContext ctx, CardGame game,
        IReadOnlyList<CardListItem> items, IReadOnlyCollection<int>? siteIds)
    {
        var cardIds = items.Select(i => i.GameCardId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var names = items.SelectMany(i => NameVariants(i.CardName)).Distinct().ToList();
        var sourceLotIds = items.Where(i => i.SourceLotId is not null).Select(i => i.SourceLotId!.Value).Distinct().ToList();
        if (cardIds.Count == 0 && names.Count == 0 && sourceLotIds.Count == 0)
            return [];

        var siteList = siteIds?.Distinct().ToList();
        var defaultSiteVisible = siteList?.Contains(Shared.Sites.Site.DefaultSiteId) ?? true;

        var rows =
            (from l in ctx.Lots.AsNoTracking()
             join p in ctx.Products.AsNoTracking() on l.ProductId equals p.Id
             where p.Category == ProductCategory.Single && p.Game == game
             where (p.GameCardId != null && cardIds.Contains(p.GameCardId)) || names.Contains(p.Name)
                   || sourceLotIds.Contains(l.Id)
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

        return rows
            .Where(r => !listedLotIds.Contains(r.Lot.Id))
            .Select(r => new Candidate(r.Lot, r.Product, r.Container,
                CardLanguages.Normalize(r.Lot.Language) ?? CardLanguages.English,
                PrintingIdentity.Key(game, r.Product.GameCardId, r.Product.SetCode, r.Product.CollectorNumber)))
            .ToList();
    }

    /// <summary>The name plus Riftbound's alternate spelling ("Vi, Piltover Enforcer" in decklists vs
    /// "Vi - Piltover Enforcer" in the catalog).</summary>
    private static string[] NameVariants(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return [];
        var dashed = name.Replace(", ", " - ");
        return dashed == name ? [name] : [name, dashed];
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
