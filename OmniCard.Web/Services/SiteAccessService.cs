using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Sites;

namespace OmniCard.Web.Services;

/// <summary>
/// A user's resolved access to every <see cref="Site"/>. Immutable snapshot — cached per user by
/// <see cref="SiteAccessService"/> and consulted for every site-scoped read and write.
///
/// <para>Rules: administrators (and the system account) are <see cref="IsUnrestricted"/> — every site,
/// write. Everyone else gets <see cref="SiteAccessLevel.Write"/> on the default site (it's always
/// visible to all users; edits there are still gated by the normal section permissions) plus whatever
/// <see cref="SiteAccessGrant"/>s their user account and role hold, taking the highest of the two.
/// A site with no grant is hidden.</para>
/// </summary>
public sealed class SiteAccess
{
    /// <summary>Every site, full write — admins, and server-side callers with no per-user identity
    /// (the loopback/OAuth MCP endpoint).</summary>
    public static readonly SiteAccess Unrestricted = new(unrestricted: true, new Dictionary<int, SiteAccessLevel>());

    private readonly IReadOnlyDictionary<int, SiteAccessLevel> _levels;

    private SiteAccess(bool unrestricted, IReadOnlyDictionary<int, SiteAccessLevel> levels)
    {
        IsUnrestricted = unrestricted;
        _levels = levels;
    }

    /// <summary>A non-admin user's access from their explicit grants (site id → level). The default
    /// site is implicitly writable and needn't appear.</summary>
    public static SiteAccess FromGrants(IReadOnlyDictionary<int, SiteAccessLevel> levels) => new(false, levels);

    public bool IsUnrestricted { get; }

    public SiteAccessLevel LevelFor(int siteId) =>
        IsUnrestricted || siteId == Site.DefaultSiteId
            ? SiteAccessLevel.Write
            : _levels.GetValueOrDefault(siteId, SiteAccessLevel.None);

    public bool CanRead(int siteId) => LevelFor(siteId) >= SiteAccessLevel.Read;

    public bool CanWrite(int siteId) => LevelFor(siteId) >= SiteAccessLevel.Write;

    /// <summary>Ids of the sites this user can at least read, or <c>null</c> when unrestricted (no
    /// filter needed). Always includes the default site.</summary>
    public IReadOnlyCollection<int>? ReadableSiteIds =>
        IsUnrestricted
            ? null
            : _levels.Where(kv => kv.Value >= SiteAccessLevel.Read).Select(kv => kv.Key)
                .Append(Site.DefaultSiteId).Distinct().ToList();

    /// <summary>The site-id filter for a query optionally narrowed to one site: <c>null</c> = no filter
    /// (unrestricted, all sites), an empty list = nothing visible (the requested site isn't readable),
    /// otherwise the readable sites — or just <paramref name="siteId"/> when given and readable.</summary>
    public IReadOnlyCollection<int>? ScopeTo(int? siteId) =>
        siteId is int id
            ? (CanRead(id) ? [id] : [])
            : ReadableSiteIds;
}

/// <summary>
/// Resolves and caches each user's <see cref="SiteAccess"/> from live DB state, mirroring
/// <see cref="PermissionService"/>: an admin's change to a site's grants, a user or a role takes effect
/// on the affected user's very next request (call <see cref="Invalidate"/> / <see cref="InvalidateAll"/>
/// after edits).
/// </summary>
public sealed class SiteAccessService(IDbContextFactory<OmniCardDbContext> factory)
{
    private readonly ConcurrentDictionary<int, SiteAccess> _cache = new();

    public SiteAccess Get(int userId) => _cache.GetOrAdd(userId, Resolve);

    /// <summary>Drop one user's cached access — after editing that user (role/admin/delete).</summary>
    public void Invalidate(int userId) => _cache.TryRemove(userId, out _);

    /// <summary>Drop every cached access — after any site grant or role change.</summary>
    public void InvalidateAll() => _cache.Clear();

    private SiteAccess Resolve(int userId)
    {
        using var db = factory.CreateDbContext();
        var user = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == userId);
        if (user is null)
            return SiteAccess.FromGrants(new Dictionary<int, SiteAccessLevel>());
        if (user.IsAdmin || user.IsSystem)
            return SiteAccess.Unrestricted;

        var roleId = user.RoleId ?? -1;
        var grants = db.SiteAccessGrants.AsNoTracking()
            .Where(g => (g.PrincipalType == SitePrincipalType.User && g.PrincipalId == userId)
                     || (g.PrincipalType == SitePrincipalType.Role && g.PrincipalId == roleId))
            .Select(g => new { g.SiteId, g.Level })
            .ToList();

        // Highest of the user's own grant and their role's grant wins.
        var levels = grants
            .GroupBy(g => g.SiteId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Level));
        return SiteAccess.FromGrants(levels);
    }
}
