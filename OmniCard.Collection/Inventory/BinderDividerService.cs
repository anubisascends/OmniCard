using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Binder;
using OmniCard.Shared.Storage;

namespace OmniCard.Collection.Inventory;

/// <summary>
/// A binder's named dividers (section bookmarks — see <see cref="BinderDivider"/>). Only the binder's
/// pages are involved: no card is read or changed. Sheet edits renumber dividers in
/// <see cref="StorageContainerService"/> (<see cref="ApplyRemap"/> / <see cref="ApplyShift"/>).
/// </summary>
public sealed class BinderDividerService(IDbContextFactory<OmniCardDbContext> dbContextFactory)
{
    /// <summary>The binder's dividers in page order (dividers on one page in the order they were added).</summary>
    public List<BinderDivider> GetForBinder(int containerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.BinderDividers.AsNoTracking()
            .Where(d => d.ContainerId == containerId)
            .OrderBy(d => d.Page).ThenBy(d => d.Id)
            .ToList();
    }

    public BinderDivider Add(int containerId, int page, string name, string? color)
    {
        using var context = dbContextFactory.CreateDbContext();
        var totalPages = TotalPagesOf(context, containerId);
        if (context.BinderDividers.Count(d => d.ContainerId == containerId) >= BinderDivider.MaxPerBinder)
            throw new InvalidOperationException($"A binder can have at most {BinderDivider.MaxPerBinder} dividers.");

        var divider = new BinderDivider
        {
            ContainerId = containerId,
            Page = CheckPage(page, totalPages),
            Name = CheckName(name),
            Color = BinderDivider.NormalizeColor(color),
        };
        context.BinderDividers.Add(divider);
        context.SaveChanges();
        return divider;
    }

    /// <summary>Renames / recolors / moves a divider; a null argument keeps that field. An empty
    /// <paramref name="color"/> clears it back to the default.</summary>
    public BinderDivider Update(int containerId, int dividerId, string? name, string? color, int? page)
    {
        using var context = dbContextFactory.CreateDbContext();
        var divider = context.BinderDividers.FirstOrDefault(d => d.Id == dividerId && d.ContainerId == containerId)
            ?? throw new KeyNotFoundException($"Divider {dividerId} not found.");

        if (name is not null) divider.Name = CheckName(name);
        if (color is not null) divider.Color = BinderDivider.NormalizeColor(color);
        if (page is int p) divider.Page = CheckPage(p, TotalPagesOf(context, containerId));
        context.SaveChanges();
        return divider;
    }

    /// <summary>Removes a divider. False when it was already gone.</summary>
    public bool Remove(int containerId, int dividerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        var divider = context.BinderDividers.FirstOrDefault(d => d.Id == dividerId && d.ContainerId == containerId);
        if (divider is null) return false;
        context.BinderDividers.Remove(divider);
        context.SaveChanges();
        return true;
    }

    /// <summary>Renumbers the binder's dividers after a sheet insert / move / remove (an old-page →
    /// new-page map from <see cref="BinderSheetLayout"/>). Tracked in <paramref name="context"/>; the
    /// caller saves, so the dividers change in the same save as the cards.</summary>
    internal static void ApplyRemap(
        OmniCardDbContext context, int containerId, IReadOnlyDictionary<int, int?> remap, int newTotalPages)
    {
        if (remap.Count == 0) return;
        foreach (var divider in context.BinderDividers.Where(d => d.ContainerId == containerId).ToList())
            divider.Page = BinderDivider.Remap(divider.Page, remap, newTotalPages);
    }

    /// <summary>Carries the dividers on the shifted pages along with their cards (Shift cards). Tracked in
    /// <paramref name="context"/>; the caller saves.</summary>
    internal static void ApplyShift(
        OmniCardDbContext context, int containerId, Func<int, bool> isMoving, int deltaPages, int totalPages)
    {
        foreach (var divider in context.BinderDividers.Where(d => d.ContainerId == containerId).ToList())
            if (isMoving(divider.Page))
                divider.Page = Math.Clamp(divider.Page + deltaPages, 1, Math.Max(1, totalPages));
    }

    private static int TotalPagesOf(OmniCardDbContext context, int containerId)
    {
        var container = context.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == containerId)
            ?? throw new KeyNotFoundException($"Container {containerId} not found.");
        if (container.ContainerType != ContainerType.Binder)
            throw new InvalidOperationException("Only binders have dividers.");
        return BinderSheetLayout.Parse(container.SheetSides, container.TotalPages).TotalPages;
    }

    private static int CheckPage(int page, int totalPages)
    {
        if (page < 1 || page > totalPages)
            throw new InvalidOperationException($"Page {page} is not in this binder (it has {totalPages}).");
        return page;
    }

    private static string CheckName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("A divider needs a name.");
        if (trimmed.Length > BinderDivider.MaxNameLength)
            throw new InvalidOperationException($"A divider name can be at most {BinderDivider.MaxNameLength} characters.");
        return trimmed;
    }
}
