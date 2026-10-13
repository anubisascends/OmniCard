using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Scanning;
using OmniCard.Shared.Settings;
using OmniCard.Shared.Sites;

namespace OmniCard.Web.Services;

/// <summary>What a collection reset removes (or, in a preview, would remove) and what it keeps.</summary>
public sealed record CollectionResetCounts(
    int Lots,
    int Units,
    int Products,
    int Movements,
    int Locations,
    int Sites,
    int Lists,
    int Trades,
    int TradeSessions,
    int Tags,
    int TagRules,
    int KeptListedLots,
    int KeptLocations,
    int KeptOrders,
    int KeptCustomers);

/// <summary>
/// Wipes the owned collection — every inventory lot (singles and sealed), storage location, non-default
/// site, card list, trade, tag and tag rule — while keeping all sales data intact (admin-only, Settings ▸
/// Reset collection). Game catalogs, users/roles/permissions, settings, deck types and scan batches are
/// never touched, and no files are deleted from disk, so restoring a database backup fully reverts it.
/// <para>
/// Sales data needs more than the sales tables: realized profit is computed from each sold lot's
/// Acquire + Sell movements and its <see cref="Product"/> (for the game), so the movements of any lot
/// that ever sold, and every product a movement or order line points at, are kept. Lots still for sale
/// (an open Listed/Picked listing, or any eBay listing record — those cascade with their lot) are kept
/// along with their tags, their location, the location they return to on cancel, and that location's
/// site. Locations referenced by settings (the for-sale location, scan-folder and open scan-batch
/// default locations) and the system Bulk location are kept too.
/// </para>
/// Everything runs in one transaction with set-based deletes (no row-version checks to trip).
/// </summary>
public sealed class CollectionResetService(
    IDbContextFactory<OmniCardDbContext> factory,
    ISalesSettingsService salesSettings,
    IScanFolderSettingsService scanFolderSettings,
    SiteAccessService siteAccess,
    ILogger<CollectionResetService> logger)
{
    /// <summary>The phrase an admin must type to confirm (checked case-sensitively).</summary>
    public const string ConfirmationPhrase = "RESET COLLECTION";

    /// <summary>What <see cref="Reset"/> would do right now, without changing anything.</summary>
    public CollectionResetCounts Preview()
    {
        using var db = factory.CreateDbContext();
        var plan = BuildPlan(db);
        var doomedLots = DoomedLots(db, plan);
        var doomedMovements = DoomedMovements(db, plan);

        // Products that survive: still referenced by a kept lot, a kept movement, or an order line.
        // Resolved in memory (the same id sets the reset's final product sweep sees once it's done).
        var survivingProductIds = new HashSet<int>(db.Lots.AsNoTracking()
            .Where(l => plan.KeptLotIds.Contains(l.Id)).Select(l => l.ProductId).ToList());
        var doomedMovementIds = doomedMovements.Select(m => m.Id).ToHashSet();
        survivingProductIds.UnionWith(db.Movements.AsNoTracking()
            .Select(m => new { m.Id, m.ProductId }).AsEnumerable()
            .Where(m => !doomedMovementIds.Contains(m.Id)).Select(m => m.ProductId));
        survivingProductIds.UnionWith(db.OrderLines.AsNoTracking()
            .Where(ol => ol.ProductId != null).Select(ol => ol.ProductId!.Value).ToList());
        var doomedProducts = db.Products.AsNoTracking().Select(p => p.Id).AsEnumerable()
            .Count(id => !survivingProductIds.Contains(id));

        var doomedTags = db.Tags.Where(t =>
            !db.LotTags.Any(lt => lt.TagId == t.Id && plan.KeptLotIds.Contains(lt.LotId)));

        return new CollectionResetCounts(
            Lots: doomedLots.Count(),
            Units: doomedLots.Sum(l => (int?)l.Quantity) ?? 0,
            Products: doomedProducts,
            Movements: doomedMovementIds.Count,
            Locations: plan.DoomedContainerIds.Count,
            Sites: plan.DoomedSiteIds.Count,
            Lists: db.CardLists.Count(),
            Trades: db.Trades.Count(),
            TradeSessions: db.TradeSessions.Count(),
            Tags: doomedTags.Count(),
            TagRules: db.TagRules.Count(),
            KeptListedLots: plan.KeptLotIds.Count,
            KeptLocations: plan.KeptContainerCount,
            KeptOrders: db.Orders.Count(),
            KeptCustomers: db.Customers.Count());
    }

    /// <summary>Resets the collection. Returns what was removed. Throws when the phrase is wrong.</summary>
    public CollectionResetCounts Reset(string confirmation, string? requestedBy)
    {
        if (!string.Equals(confirmation?.Trim(), ConfirmationPhrase, StringComparison.Ordinal))
            throw new InvalidOperationException($"Type \"{ConfirmationPhrase}\" to confirm the reset.");

        var before = Preview();
        using var db = factory.CreateDbContext();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        using var tx = db.Database.BeginTransaction();

        var plan = BuildPlan(db);
        var kept = plan.KeptLotIds;

        // Movements first (their "was this lot ever sold?" test reads the movements themselves).
        var movements = DoomedMovements(db, plan).ExecuteDelete();

        // Lots and the rows that hang off them. FlagResolutions/EbayListings also cascade in the DB;
        // EbayListings never get here because any lot with one is kept.
        db.LotTags.Where(lt => !kept.Contains(lt.LotId)).ExecuteDelete();
        db.FlagResolutions.Where(f => !kept.Contains(f.LotId)).ExecuteDelete();
        var units = DoomedLots(db, plan).Sum(l => (int?)l.Quantity) ?? 0;
        var lots = DoomedLots(db, plan).ExecuteDelete();

        // Trades go entirely; kept lots drop their links to them.
        db.Lots.Where(l => l.FulfilledTradeId != null || l.FulfilledTradeSessionId != null)
            .ExecuteUpdate(s => s.SetProperty(l => l.FulfilledTradeId, (int?)null)
                                 .SetProperty(l => l.FulfilledTradeSessionId, (int?)null));
        var trades = db.Trades.ExecuteDelete();
        var tradeSessions = db.TradeSessions.ExecuteDelete();

        // Lists, tag rules, and any tag no surviving lot still wears.
        db.CardListItems.ExecuteDelete();
        var lists = db.CardLists.ExecuteDelete();
        var tagRules = db.TagRules.ExecuteDelete();
        var tags = db.Tags.Where(t => !db.LotTags.Any(lt => lt.TagId == t.Id)).ExecuteDelete();

        // Locations (with their saved views and view defaults), then sites left with no locations.
        var doomedContainers = plan.DoomedContainerIds;
        var doomedViews = db.SavedViews.Where(v => v.ContainerId != null && doomedContainers.Contains(v.ContainerId.Value));
        db.SavedViewDefaults.Where(d => (d.ContainerId != null && doomedContainers.Contains(d.ContainerId.Value))
                                        || doomedViews.Any(v => v.Id == d.SavedViewId)).ExecuteDelete();
        doomedViews.ExecuteDelete();
        var locations = db.StorageContainers.Where(c => doomedContainers.Contains(c.Id)).ExecuteDelete();
        db.StorageContainers.Where(c => c.CoverCardId != null && !db.Lots.Any(l => l.Id == c.CoverCardId))
            .ExecuteUpdate(s => s.SetProperty(c => c.CoverCardId, (int?)null));

        var doomedSites = plan.DoomedSiteIds;
        db.SiteAccessGrants.Where(g => doomedSites.Contains(g.SiteId)).ExecuteDelete();
        var sites = db.Sites.Where(s => doomedSites.Contains(s.Id)).ExecuteDelete();

        // Products nothing points at any more (sold products stay: movements / order lines use them).
        var products = db.Products.Where(p =>
            !db.Lots.Any(l => l.ProductId == p.Id)
            && !db.Movements.Any(m => m.ProductId == p.Id)
            && !db.OrderLines.Any(ol => ol.ProductId == p.Id)).ExecuteDelete();

        tx.Commit();
        siteAccess.InvalidateAll();

        var result = before with
        {
            Lots = lots, Units = units, Products = products, Movements = movements, Locations = locations,
            Sites = sites, Lists = lists, Trades = trades, TradeSessions = tradeSessions, Tags = tags,
            TagRules = tagRules,
        };
        logger.LogWarning("Collection reset by {User}: {@Result}", requestedBy ?? "(unknown)", result);
        return result;
    }

    private sealed record Plan(
        List<int> KeptLotIds,
        List<int> DoomedContainerIds,
        int KeptContainerCount,
        List<int> DoomedSiteIds);

    /// <summary>Works out which lots, locations and sites survive. These sets are small (listed cards,
    /// locations, sites), so they're resolved in memory and sent to SQL as single id-list parameters.</summary>
    private Plan BuildPlan(OmniCardDbContext db)
    {
        var keptLotIds = db.Lots.AsNoTracking()
            .Where(l => db.Listings.Any(x => x.LotId == l.Id
                            && (x.Status == ListingStatus.Listed || x.Status == ListingStatus.Picked))
                        || db.EbayListings.Any(e => e.LotId == l.Id))
            .Select(l => l.Id)
            .ToList();

        var keptContainerIds = new HashSet<int>();
        keptContainerIds.UnionWith(db.Lots.AsNoTracking()
            .Where(l => keptLotIds.Contains(l.Id) && l.LocationId != null)
            .Select(l => l.LocationId!.Value).ToList());
        keptContainerIds.UnionWith(db.Listings.AsNoTracking()
            .Where(x => (x.Status == ListingStatus.Listed || x.Status == ListingStatus.Picked) && x.OriginalLocationId != null)
            .Select(x => x.OriginalLocationId!.Value).ToList());
        keptContainerIds.UnionWith(db.ScanBatches.AsNoTracking()
            .Where(b => b.Status != ScanBatchStatus.Committed && b.Status != ScanBatchStatus.Discarded && b.DefaultContainerId != null)
            .Select(b => b.DefaultContainerId!.Value).ToList());
        if (salesSettings.ForSaleLocationId is int forSale) keptContainerIds.Add(forSale);
        foreach (var folder in scanFolderSettings.Get().Folders)
            if (folder.DefaultContainerId is int id) keptContainerIds.Add(id);

        var containers = db.StorageContainers.AsNoTracking()
            .Select(c => new { c.Id, c.SiteId, c.IsSystem }).ToList();
        var keptContainers = containers.Where(c => c.IsSystem || keptContainerIds.Contains(c.Id)).ToList();
        var doomedContainerIds = containers.Where(c => !c.IsSystem && !keptContainerIds.Contains(c.Id))
            .Select(c => c.Id).ToList();

        var keptSiteIds = keptContainers.Select(c => c.SiteId).ToHashSet();
        var doomedSiteIds = db.Sites.AsNoTracking()
            .Where(s => !s.IsDefault && s.Id != Site.DefaultSiteId)
            .Select(s => s.Id).ToList()
            .Where(id => !keptSiteIds.Contains(id)).ToList();

        return new Plan(keptLotIds, doomedContainerIds, keptContainers.Count, doomedSiteIds);
    }

    private static IQueryable<InventoryLot> DoomedLots(OmniCardDbContext db, Plan plan) =>
        db.Lots.Where(l => !plan.KeptLotIds.Contains(l.Id));

    /// <summary>Movements of lots that are going away and never sold. Anything tied to a sale — a Sell
    /// movement, an order line, or the Acquire history (cost basis) of a lot that sold — is kept.</summary>
    private static IQueryable<InventoryMovement> DoomedMovements(OmniCardDbContext db, Plan plan)
    {
        var soldLotIds = db.Movements
            .Where(m => m.Type == MovementType.Sell && m.LotId != null)
            .Select(m => m.LotId!.Value);
        return db.Movements.Where(m =>
            m.LotId != null
            && m.OrderLineId == null
            && m.Type != MovementType.Sell
            && !plan.KeptLotIds.Contains(m.LotId.Value)
            && !soldLotIds.Contains(m.LotId.Value));
    }
}
