using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Sites;

namespace OmniCard.Web.Services;

/// <summary>
/// Create/rename/delete <see cref="Site"/>s and manage who may see them. Every mutating call here is
/// admin-only at the API layer (<c>SitesController</c>); this service only enforces the data rules:
/// unique names, the default site can't be deleted or re-permissioned (everyone always sees it), and
/// a deleted site's locations are moved to another site rather than orphaned.
/// </summary>
public sealed class SiteService(IDbContextFactory<OmniCardDbContext> factory, SiteAccessService access)
{
    /// <summary>All sites, default first, then by sort order and name.</summary>
    public List<Site> GetAll()
    {
        using var db = factory.CreateDbContext();
        return Ordered(db.Sites.AsNoTracking()).ToList();
    }

    public Site? Get(int id)
    {
        using var db = factory.CreateDbContext();
        return db.Sites.AsNoTracking().FirstOrDefault(s => s.Id == id);
    }

    /// <summary>Location count per site id (sites with no locations are absent).</summary>
    public Dictionary<int, int> LocationCounts()
    {
        using var db = factory.CreateDbContext();
        return db.StorageContainers.AsNoTracking()
            .GroupBy(c => c.SiteId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
    }

    public Site Create(string name, string? description)
    {
        var trimmed = RequireName(name);
        using var db = factory.CreateDbContext();
        EnsureNameFree(db, trimmed, excludeId: null);
        var maxSort = db.Sites.Any() ? db.Sites.Max(s => s.SortOrder) : 0;
        var site = new Site
        {
            Name = trimmed,
            Description = Clean(description),
            SortOrder = maxSort + 1,
        };
        db.Sites.Add(site);
        db.SaveChanges();
        return site;
    }

    /// <summary>Rename / re-describe a site (the default site included). Null when not found.</summary>
    public Site? Update(int id, string name, string? description)
    {
        var trimmed = RequireName(name);
        using var db = factory.CreateDbContext();
        var site = db.Sites.FirstOrDefault(s => s.Id == id);
        if (site is null) return null;
        EnsureNameFree(db, trimmed, excludeId: id);
        site.Name = trimmed;
        site.Description = Clean(description);
        db.SaveChanges();
        return site;
    }

    /// <summary>Deletes a non-default site, first moving its locations (with their cards) to
    /// <paramref name="moveToSiteId"/> (default: the default site). Its grants go with it.
    /// Returns false when not found; throws for the default site or an invalid target.</summary>
    public bool Delete(int id, int? moveToSiteId)
    {
        using var db = factory.CreateDbContext();
        var site = db.Sites.FirstOrDefault(s => s.Id == id);
        if (site is null) return false;
        if (site.IsDefault)
            throw new InvalidOperationException("The default site can't be deleted.");

        var targetId = moveToSiteId ?? Site.DefaultSiteId;
        if (targetId == id || !db.Sites.Any(s => s.Id == targetId))
            throw new InvalidOperationException("Choose another existing site to move this site's locations to.");

        foreach (var container in db.StorageContainers.Where(c => c.SiteId == id))
            container.SiteId = targetId;
        db.SiteAccessGrants.RemoveRange(db.SiteAccessGrants.Where(g => g.SiteId == id));
        db.Sites.Remove(site);
        db.SaveChanges();
        access.InvalidateAll();
        return true;
    }

    public List<SiteAccessGrant> GetGrants(int siteId)
    {
        using var db = factory.CreateDbContext();
        return db.SiteAccessGrants.AsNoTracking()
            .Where(g => g.SiteId == siteId)
            .OrderBy(g => g.PrincipalType).ThenBy(g => g.PrincipalId)
            .ToList();
    }

    /// <summary>Replaces every grant on a site. <see cref="SiteAccessLevel.None"/> entries are dropped
    /// (no grant = hidden); duplicates for one principal keep the highest level. Throws for the default
    /// site, which is always visible to everyone and takes no grants.</summary>
    public void SetGrants(int siteId, IEnumerable<(SitePrincipalType Type, int PrincipalId, SiteAccessLevel Level)> grants)
    {
        using var db = factory.CreateDbContext();
        var site = db.Sites.FirstOrDefault(s => s.Id == siteId)
            ?? throw new KeyNotFoundException($"Site {siteId} not found.");
        if (site.IsDefault)
            throw new InvalidOperationException("The default site is always visible to every user; it has no access list.");

        var wanted = grants
            .Where(g => g.Level > SiteAccessLevel.None)
            .GroupBy(g => (g.Type, g.PrincipalId))
            .Select(g => new SiteAccessGrant
            {
                SiteId = siteId,
                PrincipalType = g.Key.Type,
                PrincipalId = g.Key.PrincipalId,
                Level = g.Max(x => x.Level),
            })
            .ToList();

        db.SiteAccessGrants.RemoveRange(db.SiteAccessGrants.Where(g => g.SiteId == siteId));
        db.SaveChanges();
        db.SiteAccessGrants.AddRange(wanted);
        db.SaveChanges();
        access.InvalidateAll();
    }

    /// <summary>Removes every grant held by a user or role — call when that user/role is deleted so a
    /// later account reusing the id can't inherit access.</summary>
    public void RemovePrincipal(SitePrincipalType type, int principalId)
    {
        using var db = factory.CreateDbContext();
        db.SiteAccessGrants.RemoveRange(db.SiteAccessGrants
            .Where(g => g.PrincipalType == type && g.PrincipalId == principalId));
        db.SaveChanges();
        access.InvalidateAll();
    }

    internal static IQueryable<Site> Ordered(IQueryable<Site> sites) =>
        sites.OrderByDescending(s => s.IsDefault).ThenBy(s => s.SortOrder).ThenBy(s => s.Name);

    private static string RequireName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("A site name is required.");
        if (trimmed.Length > 200)
            throw new InvalidOperationException("Site names are limited to 200 characters.");
        return trimmed;
    }

    private static string? Clean(string? description)
    {
        var d = description?.Trim();
        return string.IsNullOrEmpty(d) ? null : d.Length > 1000 ? d[..1000] : d;
    }

    private static void EnsureNameFree(OmniCardDbContext db, string name, int? excludeId)
    {
        var taken = db.Sites.AsNoTracking()
            .Where(s => excludeId == null || s.Id != excludeId)
            .Select(s => s.Name)
            .AsEnumerable()
            .Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (taken)
            throw new InvalidOperationException($"A site named \"{name}\" already exists.");
    }
}
