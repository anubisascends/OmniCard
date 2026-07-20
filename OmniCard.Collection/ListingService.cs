using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class ListingService(
    IDbContextFactory<OmniCardDbContext> dbContextFactory,
    ISalesSettingsService salesSettings) : IListingService
{
    private static readonly ListingStatus[] ActiveStatuses = [ListingStatus.Listed, ListingStatus.Picked];

    public int ListForSale(IEnumerable<int> lotIds, SalesChannel channel, decimal price, int quantity, string? note = null)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var ids = lotIds.Distinct().ToList();

        var alreadyListed = ctx.Listings
            .Where(l => ids.Contains(l.LotId) && ActiveStatuses.Contains(l.Status))
            .Select(l => l.LotId)
            .ToHashSet();

        var lotLocations = ctx.Lots
            .Where(l => ids.Contains(l.Id))
            .ToDictionary(l => l.Id, l => l.LocationId);

        var created = 0;
        foreach (var lotId in ids)
        {
            if (alreadyListed.Contains(lotId) || !lotLocations.ContainsKey(lotId))
                continue;

            ctx.Listings.Add(new Listing
            {
                LotId = lotId,
                Channel = channel,
                Status = ListingStatus.Listed,
                ListedPrice = price,
                Quantity = quantity,
                OriginalLocationId = lotLocations[lotId],
                ListedAt = DateTime.UtcNow,
                Note = note,
            });
            created++;
        }

        ctx.SaveChanges();
        return created;
    }

    public void Unlist(IEnumerable<int> lotIds)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var ids = lotIds.Distinct().ToList();

        var listings = ctx.Listings
            .Where(l => ids.Contains(l.LotId) && ActiveStatuses.Contains(l.Status))
            .ToList();

        foreach (var listing in listings)
        {
            // If already picked, physically return it to its original location.
            if (listing.Status == ListingStatus.Picked && listing.OriginalLocationId is not null)
            {
                var lot = ctx.Lots.FirstOrDefault(l => l.Id == listing.LotId);
                if (lot is not null && lot.LocationId != listing.OriginalLocationId)
                {
                    // The original container may have been deleted since the listing was
                    // created (InventoryLot.LocationId has an enforced FK to
                    // StorageContainers.Id). Restore to unassigned rather than a dangling
                    // id, which would FK-violate on SaveChanges.
                    var originalContainerExists = ctx.StorageContainers.Any(c => c.Id == listing.OriginalLocationId);
                    lot.LocationId = originalContainerExists ? listing.OriginalLocationId : null;
                    ctx.Movements.Add(new InventoryMovement
                    {
                        ProductId = lot.ProductId,
                        LotId = lot.Id,
                        Type = MovementType.Move,
                        Quantity = lot.Quantity,
                        Timestamp = DateTime.UtcNow,
                        Note = "Unlisted — returned to original location",
                    });
                }
            }
            listing.Status = ListingStatus.Cancelled;
        }

        ctx.SaveChanges();
    }

    public int MarkPicked(IEnumerable<int> lotIds)
    {
        var forSaleLocationId = salesSettings.ForSaleLocationId
            ?? throw new InvalidOperationException("No 'For Sale' location is configured. Set one in Sales settings before picking.");

        using var ctx = dbContextFactory.CreateDbContext();

        if (!ctx.StorageContainers.Any(c => c.Id == forSaleLocationId))
            throw new InvalidOperationException("The configured For-Sale location no longer exists. Pick a new one in the Sales tab.");

        var ids = lotIds.Distinct().ToList();

        var listings = ctx.Listings
            .Where(l => ids.Contains(l.LotId) && l.Status == ListingStatus.Listed)
            .ToList();

        var picked = 0;
        foreach (var listing in listings)
        {
            var lot = ctx.Lots.FirstOrDefault(l => l.Id == listing.LotId);
            if (lot is null) continue;

            listing.Status = ListingStatus.Picked;
            listing.PickedAt = DateTime.UtcNow;
            lot.LocationId = forSaleLocationId;
            ctx.Movements.Add(new InventoryMovement
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                Type = MovementType.Move,
                Quantity = lot.Quantity,
                Timestamp = DateTime.UtcNow,
                Note = "Picked for sale",
            });
            picked++;
        }

        ctx.SaveChanges();
        return picked;
    }

    public List<PickListEntry> GetPickList(CardGame? game = null)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var query =
            from listing in ctx.Listings.AsNoTracking()
            where listing.Status == ListingStatus.Listed
            join lot in ctx.Lots.AsNoTracking() on listing.LotId equals lot.Id
            join p in ctx.Products.AsNoTracking() on lot.ProductId equals p.Id
            join sc in ctx.StorageContainers.AsNoTracking() on lot.LocationId equals sc.Id into scj
            from sc in scj.DefaultIfEmpty()
            where game == null || p.Game == game
            orderby sc != null ? sc.Name : "(unassigned)", lot.Section, lot.Page, lot.Slot
            select new PickListEntry(
                lot.Id,
                p.Name,
                p.SetName ?? "",
                p.SetCode ?? "",
                lot.Condition,
                p.Foil,
                sc != null ? sc.Name : "(unassigned)",
                lot.Section,
                lot.Page,
                lot.Slot,
                listing.ListedPrice,
                listing.Quantity);

        return query.ToList();
    }

    public Dictionary<int, ListingStatus> GetActiveListingStatusByLot(IEnumerable<int> lotIds)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var ids = lotIds.Distinct().ToList();
        return ctx.Listings.AsNoTracking()
            .Where(l => ids.Contains(l.LotId) && ActiveStatuses.Contains(l.Status))
            .ToList()
            .GroupBy(l => l.LotId)
            .ToDictionary(g => g.Key, g => g.Max(l => l.Status));
    }

    public List<ActiveListing> GetActiveListings(CardGame? game = null)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var query =
            from listing in ctx.Listings.AsNoTracking()
            where ActiveStatuses.Contains(listing.Status)
            join lot in ctx.Lots.AsNoTracking() on listing.LotId equals lot.Id
            join p in ctx.Products.AsNoTracking() on lot.ProductId equals p.Id
            where game == null || p.Game == game
            orderby p.Name
            select new ActiveListing(
                lot.Id,
                p.Name,
                p.SetName ?? "",
                p.SetCode ?? "",
                lot.Condition,
                p.Foil,
                listing.ListedPrice,
                listing.Status);
        return query.ToList();
    }

    public void MarkSold(int lotId, int orderLineId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var listing = ctx.Listings
            .Where(l => l.LotId == lotId && ActiveStatuses.Contains(l.Status))
            .OrderByDescending(l => l.Status)
            .FirstOrDefault();
        if (listing is null) return;
        listing.Status = ListingStatus.Sold;
        listing.OrderLineId = orderLineId;
        ctx.SaveChanges();
    }
}
