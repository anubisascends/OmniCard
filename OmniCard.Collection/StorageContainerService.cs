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

    public StorageContainer Create(string name, ContainerType type)
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
            SortOrder = maxSort + 1
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
}
