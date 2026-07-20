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
                    lot.LocationId = listing.OriginalLocationId;
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
    public List<PickListEntry> GetPickList(CardGame? game = null) => throw new NotImplementedException();
    public Dictionary<int, ListingStatus> GetActiveListingStatusByLot(IEnumerable<int> lotIds) => throw new NotImplementedException();
}
