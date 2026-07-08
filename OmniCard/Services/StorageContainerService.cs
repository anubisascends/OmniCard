using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Services;

public sealed class StorageContainerService(IDbContextFactory<CollectionDbContext> dbContextFactory)
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

        var cards = context.Cards.Where(c => c.ContainerId == id).ToList();

        if (moveCardsToBulk)
        {
            var bulkId = context.StorageContainers.First(c => c.IsSystem).Id;
            foreach (var card in cards)
            {
                card.ContainerId = bulkId;
                card.Page = null;
                card.Slot = null;
                card.Section = null;
            }
        }
        else
        {
            context.Cards.RemoveRange(cards);
        }

        context.StorageContainers.Remove(container);
        context.SaveChanges();
    }

    public int GetCardCount(int containerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.Cards.Count(c => c.ContainerId == containerId);
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
        return context.Cards
            .AsNoTracking()
            .Where(c => c.ContainerId == containerId)
            .OrderBy(c => c.Name)
            .ToList();
    }
}
