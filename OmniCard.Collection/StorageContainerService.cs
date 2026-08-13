using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public sealed class StorageContainerService(IDbContextFactory<OmniCardDbContext> dbContextFactory)
    : IStorageContainerService
{
    public List<StorageContainer> GetAll()
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.StorageContainers
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToList();
    }

    public StorageContainer GetBulk()
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.StorageContainers.First(c => c.IsSystem);
    }

    public StorageContainer Create(string name, ContainerType type, int slotsPerPage = 9)
    {
        using var context = dbContextFactory.CreateDbContext();
        var maxSort = context.StorageContainers.Any()
            ? context.StorageContainers.Max(c => c.SortOrder)
            : 0;

        var container = new StorageContainer
        {
            Name = name,
            ContainerType = type,
            IsSystem = false,
            SortOrder = maxSort + 1,
            SlotsPerPage = slotsPerPage > 0 ? slotsPerPage : 9
        };

        context.StorageContainers.Add(container);
        context.SaveChanges();
        return container;
    }

    public void Rename(int id, string newName)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(id)
            ?? throw new InvalidOperationException($"Container {id} not found");
        if (container.IsSystem)
            throw new InvalidOperationException("Cannot rename system container");

        container.Name = newName;
        context.SaveChanges();
    }

    public void Delete(int id, bool moveCardsToBulk = true)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(id)
            ?? throw new InvalidOperationException($"Container {id} not found");
        if (container.IsSystem)
            throw new InvalidOperationException("Cannot delete system container");

        var lots = context.Lots.Include(l => l.Product)
            .Where(l => l.LocationId == id && l.Product.Category == ProductCategory.Single)
            .ToList();

        if (moveCardsToBulk)
        {
            var bulkId = context.StorageContainers.First(c => c.IsSystem).Id;
            foreach (var lot in lots)
            {
                lot.LocationId = bulkId;
                lot.Page = null;
                lot.Slot = null;
                lot.Section = null;
            }
        }
        else
        {
            var lotIds = lots.Select(l => l.Id).ToList();
            context.EbayListings.RemoveRange(context.EbayListings.Where(e => lotIds.Contains(e.LotId)));
            context.FlagResolutions.RemoveRange(context.FlagResolutions.Where(f => lotIds.Contains(f.LotId)));
            context.Lots.RemoveRange(lots);
        }

        context.StorageContainers.Remove(container);
        context.SaveChanges();
    }

    public int GetCardCount(int containerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.Lots.Count(l => l.LocationId == containerId && l.Product.Category == ProductCategory.Single);
    }

    public void SetCoverCard(int containerId, int? cardId)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        container.CoverCardId = cardId;
        context.SaveChanges();
    }

    public List<CollectionCard> GetCardsInContainer(int containerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.Lots.AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.LocationId == containerId && l.Product.Category == ProductCategory.Single)
            .ToList()
            .Select(l => CollectionCardMapper.ToDto(l, l.Product, 0m))
            .OrderBy(c => c.Name)
            .ToList();
    }

    public void SetExcludeFromDeckCheck(int containerId, bool exclude)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        container.ExcludeFromDeckCheck = exclude;
        context.SaveChanges();
    }

    public BinderLayout GetBinderLayout(int containerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        return new BinderLayout { SlotsPerPage = container.SlotsPerPage, TotalPages = container.TotalPages, Columns = container.Columns };
    }

    public void AddBinderPage(int containerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        container.AddPage();
        context.SaveChanges();
    }

    public void SetSlotsPerPage(int containerId, int slotsPerPage)
    {
        if (slotsPerPage <= 0)
            throw new ArgumentOutOfRangeException(nameof(slotsPerPage), "Slots per page must be positive.");

        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        container.SlotsPerPage = slotsPerPage;
        context.SaveChanges();
    }

    public void SetColumns(int containerId, int columns)
    {
        if (columns <= 0)
            throw new ArgumentOutOfRangeException(nameof(columns), "Columns must be positive.");

        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        container.Columns = columns;
        context.SaveChanges();
    }

    public List<CollectionCard> GetPlacedCardsOnPage(int containerId, int page)
    {
        using var context = dbContextFactory.CreateDbContext();
        var cards = context.Lots.AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.LocationId == containerId && l.Page == page && l.Product.Category == ProductCategory.Single)
            .ToList()
            .Select(l => CollectionCardMapper.ToDto(l, l.Product, 0m))
            .ToList();

        // Attach eBay listing state so binder-slot tiles/context-menu actions (List/View/End on
        // eBay) see accurate status, same as the main collection list.
        var lotIds = cards.Select(c => c.Id).ToList();
        var listingsByLotId = context.EbayListings.AsNoTracking()
            .Where(l => lotIds.Contains(l.LotId))
            .ToList()
            .ToDictionary(l => l.LotId);
        foreach (var card in cards)
            card.EbayListing = listingsByLotId.GetValueOrDefault(card.Id);

        return cards;
    }

    public void AssignCardToSlot(int lotId, int containerId, int page, int slot)
    {
        using var context = dbContextFactory.CreateDbContext();

        var draggedLot = context.Lots.Find(lotId)
            ?? throw new InvalidOperationException($"Lot {lotId} not found");
        if (draggedLot.LocationId != containerId)
            throw new InvalidOperationException($"Lot {lotId} is not in container {containerId}");

        var occupant = context.Lots.FirstOrDefault(l =>
            l.LocationId == containerId && l.Page == page && l.Slot == slot && l.Id != lotId);

        if (occupant is not null)
        {
            if (draggedLot.Page is null && draggedLot.Slot is null)
            {
                // Dragged from the unplaced pool: displaced occupant returns to the pool.
                occupant.Page = null;
                occupant.Slot = null;
            }
            else
            {
                // Dragged from another slot: swap coordinates.
                occupant.Page = draggedLot.Page;
                occupant.Slot = draggedLot.Slot;
            }
        }

        draggedLot.LocationId = containerId;
        draggedLot.Page = page;
        draggedLot.Slot = slot;

        context.SaveChanges();
    }

    public void UnassignFromPage(int lotId)
    {
        using var context = dbContextFactory.CreateDbContext();
        var lot = context.Lots.Find(lotId)
            ?? throw new InvalidOperationException($"Lot {lotId} not found");
        lot.Page = null;
        lot.Slot = null;
        context.SaveChanges();
    }
}
