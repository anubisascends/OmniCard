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

        // New binders start with one double-sided sheet (front + back), the default the user asked
        // for; non-binder containers ignore this and keep the single-page default.
        if (type == ContainerType.Binder)
        {
            var layout = BinderSheetLayout.NewDefault();
            container.SheetSides = layout.Serialize();
            container.TotalPages = layout.TotalPages;
        }

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

    public void SetAlwaysAvailable(int containerId, bool alwaysAvailable)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        // The system Bulk location is always available intrinsically; leave its stored flag alone.
        if (container.IsSystem)
            return;
        container.AlwaysAvailable = alwaysAvailable;
        context.SaveChanges();
    }

    public BinderLayout GetBinderLayout(int containerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        var sheets = BinderSheetLayout.Parse(container.SheetSides, container.TotalPages);
        return new BinderLayout
        {
            SlotsPerPage = container.SlotsPerPage,
            TotalPages = sheets.TotalPages,
            Columns = container.Columns,
            SheetSides = sheets.Sides,
        };
    }

    public void AddBinderSheet(int containerId, bool doubleSided)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        // Appending a sheet never moves an existing page, so no lot remapping is needed.
        var layout = BinderSheetLayout.Parse(container.SheetSides, container.TotalPages).Append(doubleSided);
        container.SheetSides = layout.Serialize();
        container.TotalPages = layout.TotalPages;
        context.SaveChanges();
    }

    public BinderSheetInfo GetSheetForPage(int containerId, int page)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        var layout = BinderSheetLayout.Parse(container.SheetSides, container.TotalPages);
        var sheetIndex = layout.SheetIndexOfPage(page);
        if (sheetIndex < 0)
            throw new InvalidOperationException($"Page {page} is not in binder {containerId}.");

        var firstPage = layout.FirstPageOfSheet(sheetIndex);
        var sides = layout.SidesOfSheet(sheetIndex);
        var lastPageExclusive = firstPage + sides;
        var cardCount = context.Lots.Count(l => l.LocationId == containerId
            && l.Page >= firstPage && l.Page < lastPageExclusive
            && l.Product.Category == ProductCategory.Single);

        return new BinderSheetInfo
        {
            SheetIndex = sheetIndex,
            FirstPage = firstPage,
            Sides = sides,
            TotalSheets = layout.SheetCount,
            CardCount = cardCount,
            Pages = Enumerable.Range(firstPage, sides).ToList(),
        };
    }

    public List<BinderSheetInfo> GetSheets(int containerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        var layout = BinderSheetLayout.Parse(container.SheetSides, container.TotalPages);

        var placedPages = context.Lots.AsNoTracking()
            .Where(l => l.LocationId == containerId && l.Page != null && l.Product.Category == ProductCategory.Single)
            .Select(l => l.Page)
            .ToList();
        var counts = placedPages.Where(p => p.HasValue)
            .GroupBy(p => p!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var sheets = new List<BinderSheetInfo>();
        for (var i = 0; i < layout.SheetCount; i++)
        {
            var first = layout.FirstPageOfSheet(i);
            var sides = layout.SidesOfSheet(i);
            var pages = Enumerable.Range(first, sides).ToList();
            sheets.Add(new BinderSheetInfo
            {
                SheetIndex = i,
                FirstPage = first,
                Sides = sides,
                TotalSheets = layout.SheetCount,
                CardCount = pages.Sum(p => counts.GetValueOrDefault(p)),
                Pages = pages,
            });
        }

        return sheets;
    }

    public void InsertBinderSheet(int containerId, int insertIndex, bool doubleSided)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        var layout = BinderSheetLayout.Parse(container.SheetSides, container.TotalPages);
        if (insertIndex < 0 || insertIndex > layout.SheetCount)
            throw new ArgumentOutOfRangeException(nameof(insertIndex));

        var (newLayout, remap) = layout.InsertSheet(insertIndex, doubleSided);
        ApplyPageRemap(context, containerId, newLayout, remap);
    }

    public void MoveBinderSheet(int containerId, int fromPage, int toIndex)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        var layout = BinderSheetLayout.Parse(container.SheetSides, container.TotalPages);
        var fromIndex = layout.SheetIndexOfPage(fromPage);
        if (fromIndex < 0)
            throw new InvalidOperationException($"Page {fromPage} is not in binder {containerId}.");
        if (toIndex < 0 || toIndex >= layout.SheetCount)
            throw new ArgumentOutOfRangeException(nameof(toIndex));

        var (newLayout, remap) = layout.MoveSheet(fromIndex, toIndex);
        ApplyPageRemap(context, containerId, newLayout, remap);
    }

    public void RemoveBinderSheet(int containerId, int page)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        var layout = BinderSheetLayout.Parse(container.SheetSides, container.TotalPages);
        var sheetIndex = layout.SheetIndexOfPage(page);
        if (sheetIndex < 0)
            throw new InvalidOperationException($"Page {page} is not in binder {containerId}.");
        if (layout.SheetCount <= 1)
            throw new InvalidOperationException("A binder must keep at least one page.");

        var (newLayout, remap) = layout.RemoveSheet(sheetIndex);
        ApplyPageRemap(context, containerId, newLayout, remap);
    }

    /// <summary>Applies a page-number remap (from a <see cref="BinderSheetLayout"/> transform) to
    /// every placed lot in the binder in one pass, then persists the new sheet layout. A null
    /// target unplaces the lot (Page/Slot cleared, staying in the binder); any other target renames
    /// the page, keeping the slot. Shared by remove/insert/move.</summary>
    private static void ApplyPageRemap(
        OmniCardDbContext context, int containerId, BinderSheetLayout newLayout,
        IReadOnlyDictionary<int, int?> pageRemap)
    {
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        if (pageRemap.Count > 0)
        {
            var placed = context.Lots.Where(l => l.LocationId == containerId && l.Page != null).ToList();
            foreach (var lot in placed)
            {
                if (lot.Page is int p && pageRemap.TryGetValue(p, out var newPage))
                {
                    if (newPage is null) { lot.Page = null; lot.Slot = null; }
                    else lot.Page = newPage.Value;
                }
            }
        }

        container.SheetSides = newLayout.Serialize();
        container.TotalPages = newLayout.TotalPages;
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
