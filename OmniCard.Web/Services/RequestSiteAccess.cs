using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Sites;

namespace OmniCard.Web.Services;

/// <summary>
/// Per-request view of the signed-in user's <see cref="SiteAccess"/>, plus the lookups the API needs
/// to check a location or a set of cards against it (location → site, lot → location → site). Scoped:
/// resolved once per request. A request with no signed-in user only sees the default site.
///
/// <para>Missing ids are treated as "no site to check" (the lookup returns nothing) so the endpoint's
/// own not-found handling still applies; a lot with no location counts as being in the default site.</para>
/// </summary>
public sealed class RequestSiteAccess(
    IHttpContextAccessor http,
    SiteAccessService sites,
    IDbContextFactory<OmniCardDbContext> factory)
{
    private SiteAccess? _current;

    /// <summary>The current user's access (resolved lazily, cached for the request).</summary>
    public SiteAccess Current => _current ??= Resolve();

    private SiteAccess Resolve()
    {
        var ctx = http.HttpContext;
        var userId = ctx is null ? null : AppAuthGate.CurrentUserId(ctx);
        return userId is int id
            ? sites.Get(id)
            : SiteAccess.FromGrants(new Dictionary<int, SiteAccessLevel>());
    }

    /// <summary>The site a location belongs to, or null when the location doesn't exist.</summary>
    public int? SiteOfLocation(int containerId)
    {
        using var db = factory.CreateDbContext();
        return db.StorageContainers.AsNoTracking()
            .Where(c => c.Id == containerId)
            .Select(c => (int?)c.SiteId)
            .FirstOrDefault();
    }

    /// <summary>The distinct sites the given lots live in (unlocated lots → default site). Unknown ids
    /// contribute nothing.</summary>
    public IReadOnlyList<int> SitesOfLots(IEnumerable<int> lotIds)
    {
        var ids = lotIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        using var db = factory.CreateDbContext();
        return (from l in db.Lots.AsNoTracking()
                where ids.Contains(l.Id)
                join c in db.StorageContainers.AsNoTracking() on l.LocationId equals c.Id into j
                from c in j.DefaultIfEmpty()
                select c == null ? Site.DefaultSiteId : c.SiteId)
            .Distinct()
            .ToList();
    }

    public bool CanReadLocation(int containerId) =>
        Current.IsUnrestricted || SiteOfLocation(containerId) is not int site || Current.CanRead(site);

    public bool CanWriteLocation(int containerId) =>
        Current.IsUnrestricted || SiteOfLocation(containerId) is not int site || Current.CanWrite(site);

    public bool CanReadLots(IEnumerable<int> lotIds) =>
        Current.IsUnrestricted || SitesOfLots(lotIds).All(Current.CanRead);

    public bool CanWriteLots(IEnumerable<int> lotIds) =>
        Current.IsUnrestricted || SitesOfLots(lotIds).All(Current.CanWrite);
}
