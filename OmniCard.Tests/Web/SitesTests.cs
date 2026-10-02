using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Collection.Inventory;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Settings;
using OmniCard.Shared.Sites;
using OmniCard.Shared.Storage;
using OmniCard.Web.Api.Controllers;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>
/// Sites — major physical locations (homes, shops) holding many storage locations — and the per-user
/// visibility rules layered on them: the seeded default site, admin-only management data rules,
/// user/role grant resolution, and the site filter every collection/location read applies.
/// </summary>
public class SitesTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly MockFactory _factory;
    private readonly SiteAccessService _access;
    private readonly SiteService _sites;

    public SitesTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();
        _factory = new MockFactory(_opts);
        _access = new SiteAccessService(_factory);
        _sites = new SiteService(_factory, _access);
    }

    public void Dispose() => _conn.Dispose();

    // --- helpers -------------------------------------------------------------------------------

    private OmniCardDbContext Db() => new(_opts);

    private int AddUser(string name, bool isAdmin = false, int? roleId = null)
    {
        using var db = Db();
        var u = new User { Username = name, PasswordHash = "x", IsAdmin = isAdmin, RoleId = roleId };
        db.Users.Add(u);
        db.SaveChanges();
        return u.Id;
    }

    private int AddRole(string name)
    {
        using var db = Db();
        var r = new Role { Name = name };
        db.Roles.Add(r);
        db.SaveChanges();
        return r.Id;
    }

    private int AddLocation(string name, int siteId)
    {
        using var db = Db();
        var c = new StorageContainer { Name = name, ContainerType = ContainerType.Box, SiteId = siteId };
        db.StorageContainers.Add(c);
        db.SaveChanges();
        return c.Id;
    }

    private int AddLot(string cardName, int? locationId)
    {
        using var db = Db();
        var p = new Product
        {
            Game = CardGame.Pokemon, Category = ProductCategory.Single,
            GameCardId = cardName.ToLowerInvariant(), Name = cardName, SetCode = "BASE", CollectorNumber = "1",
        };
        db.Products.Add(p);
        db.SaveChanges();
        var lot = new InventoryLot { ProductId = p.Id, Quantity = 1, Condition = "NM", LocationId = locationId };
        db.Lots.Add(lot);
        db.SaveChanges();
        return lot.Id;
    }

    private RequestSiteAccess RequestFor(int userId)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test")),
        };
        return new RequestSiteAccess(new HttpContextAccessor { HttpContext = http }, _access, _factory);
    }

    // --- schema / default site -----------------------------------------------------------------

    [Fact]
    public void DefaultSite_IsSeeded_AndNewLocationsLandInIt()
    {
        var site = Assert.Single(_sites.GetAll());
        Assert.Equal(Site.DefaultSiteId, site.Id);
        Assert.True(site.IsDefault);
        Assert.Equal(Site.DefaultSiteName, site.Name);

        var created = new StorageContainerService(_factory).Create("Binder A", ContainerType.Binder);
        Assert.Equal(Site.DefaultSiteId, created.SiteId);
    }

    [Fact]
    public void StorageContainerService_CreateInSite_AndSetSite()
    {
        var home = _sites.Create("Andrew's House", null);
        var svc = new StorageContainerService(_factory);
        var box = svc.Create("Shoebox", ContainerType.Box, siteId: home.Id);
        Assert.Equal(home.Id, box.SiteId);

        svc.SetSite(box.Id, Site.DefaultSiteId);
        using var db = Db();
        Assert.Equal(Site.DefaultSiteId, db.StorageContainers.Single(c => c.Id == box.Id).SiteId);
    }

    [Fact]
    public void StorageContainerService_SetSite_RefusesBulk()
    {
        using (var db = Db())
        {
            db.StorageContainers.Add(new StorageContainer { Name = "Bulk", ContainerType = ContainerType.Bulk, IsSystem = true });
            db.SaveChanges();
        }
        var home = _sites.Create("Home", null);
        var svc = new StorageContainerService(_factory);
        Assert.Throws<InvalidOperationException>(() => svc.SetSite(svc.GetBulk().Id, home.Id));
    }

    // --- SiteService rules ---------------------------------------------------------------------

    [Fact]
    public void Create_RejectsDuplicateNames_CaseInsensitive()
    {
        _sites.Create("Lake House", null);
        Assert.Throws<InvalidOperationException>(() => _sites.Create("lake house", null));
        Assert.Throws<InvalidOperationException>(() => _sites.Create("default", null));
    }

    [Fact]
    public void Delete_MovesLocationsToTarget_AndDropsGrants()
    {
        var a = _sites.Create("A", null);
        var b = _sites.Create("B", null);
        var loc = AddLocation("Box in A", a.Id);
        _sites.SetGrants(a.Id, [(SitePrincipalType.User, 42, SiteAccessLevel.Read)]);

        Assert.True(_sites.Delete(a.Id, b.Id));

        using var db = Db();
        Assert.Equal(b.Id, db.StorageContainers.Single(c => c.Id == loc).SiteId);
        Assert.Empty(db.SiteAccessGrants.Where(g => g.SiteId == a.Id));
        Assert.False(db.Sites.Any(s => s.Id == a.Id));
    }

    [Fact]
    public void Delete_DefaultsTargetToDefaultSite()
    {
        var a = _sites.Create("A", null);
        var loc = AddLocation("Box", a.Id);
        _sites.Delete(a.Id, moveToSiteId: null);
        using var db = Db();
        Assert.Equal(Site.DefaultSiteId, db.StorageContainers.Single(c => c.Id == loc).SiteId);
    }

    [Fact]
    public void DefaultSite_CannotBeDeleted_OrPermissioned()
    {
        Assert.Throws<InvalidOperationException>(() => _sites.Delete(Site.DefaultSiteId, null));
        Assert.Throws<InvalidOperationException>(() =>
            _sites.SetGrants(Site.DefaultSiteId, [(SitePrincipalType.User, 1, SiteAccessLevel.Read)]));
    }

    [Fact]
    public void SetGrants_DropsNone_AndKeepsHighestPerPrincipal()
    {
        var a = _sites.Create("A", null);
        _sites.SetGrants(a.Id,
        [
            (SitePrincipalType.User, 7, SiteAccessLevel.Read),
            (SitePrincipalType.User, 7, SiteAccessLevel.Write),
            (SitePrincipalType.Role, 3, SiteAccessLevel.None),
        ]);
        var grant = Assert.Single(_sites.GetGrants(a.Id));
        Assert.Equal(SiteAccessLevel.Write, grant.Level);
        Assert.Equal(SitePrincipalType.User, grant.PrincipalType);
    }

    // --- access resolution ---------------------------------------------------------------------

    [Fact]
    public void Admin_IsUnrestricted()
    {
        var admin = AddUser("boss", isAdmin: true);
        var hidden = _sites.Create("Hidden", null);
        var access = _access.Get(admin);
        Assert.True(access.IsUnrestricted);
        Assert.True(access.CanWrite(hidden.Id));
        Assert.Null(access.ReadableSiteIds);
    }

    [Fact]
    public void User_WithNoGrants_SeesOnlyDefaultSite()
    {
        var kid = AddUser("kid");
        var mine = _sites.Create("Andrew's House", null);
        var access = _access.Get(kid);

        Assert.True(access.CanWrite(Site.DefaultSiteId));
        Assert.False(access.CanRead(mine.Id));
        Assert.Equal([Site.DefaultSiteId], access.ReadableSiteIds!.ToList());
    }

    [Fact]
    public void Grants_FromUserAndRole_TakeTheHighest()
    {
        var family = AddRole("Family");
        var kid = AddUser("kid", roleId: family);
        var mine = _sites.Create("Andrew's House", null);
        var hers = _sites.Create("Partner's House", null);
        var kidsRoom = _sites.Create("Kids' Room", null);

        _sites.SetGrants(mine.Id, [(SitePrincipalType.Role, family, SiteAccessLevel.Read)]);
        _sites.SetGrants(kidsRoom.Id,
        [
            (SitePrincipalType.Role, family, SiteAccessLevel.Read),
            (SitePrincipalType.User, kid, SiteAccessLevel.Write),
        ]);

        var access = _access.Get(kid);
        Assert.Equal(SiteAccessLevel.Read, access.LevelFor(mine.Id));
        Assert.Equal(SiteAccessLevel.None, access.LevelFor(hers.Id));
        Assert.Equal(SiteAccessLevel.Write, access.LevelFor(kidsRoom.Id));
        Assert.Equal(new[] { Site.DefaultSiteId, mine.Id, kidsRoom.Id }.Order(), access.ReadableSiteIds!.Order());
    }

    [Fact]
    public void GrantChanges_TakeEffectImmediately()
    {
        var user = AddUser("partner");
        var hers = _sites.Create("Partner's House", null);
        Assert.False(_access.Get(user).CanRead(hers.Id)); // cached now

        _sites.SetGrants(hers.Id, [(SitePrincipalType.User, user, SiteAccessLevel.Write)]);
        Assert.True(_access.Get(user).CanWrite(hers.Id));

        _sites.RemovePrincipal(SitePrincipalType.User, user);
        Assert.False(_access.Get(user).CanRead(hers.Id));
    }

    [Fact]
    public void ScopeTo_NarrowsToOneReadableSite_OrNothing()
    {
        var user = AddUser("u");
        var a = _sites.Create("A", null);
        var b = _sites.Create("B", null);
        _sites.SetGrants(a.Id, [(SitePrincipalType.User, user, SiteAccessLevel.Read)]);
        var access = _access.Get(user);

        Assert.Equal([a.Id], access.ScopeTo(a.Id));
        Assert.Empty(access.ScopeTo(b.Id)!);
        Assert.Null(SiteAccess.Unrestricted.ScopeTo(null));
    }

    // --- read filtering ------------------------------------------------------------------------

    [Fact]
    public void CollectionQuery_SiteFilter_HidesOtherSites_AndTreatsUnlocatedAsDefault()
    {
        var mine = _sites.Create("Mine", null);
        var hers = _sites.Create("Hers", null);
        AddLot("In Default", AddLocation("Default Box", Site.DefaultSiteId));
        AddLot("In Mine", AddLocation("My Box", mine.Id));
        AddLot("In Hers", AddLocation("Her Box", hers.Id));
        AddLot("Unlocated", null);

        using var ctx = Db();
        string[] Names(IReadOnlyCollection<int>? siteIds) =>
            CollectionQueryBuilder.BuildFilteredQuery(ctx, "", null, null, null, siteIds: siteIds)
                .Select(c => c.Name).AsEnumerable().Order().ToArray();

        Assert.Equal(["In Default", "In Hers", "In Mine", "Unlocated"], Names(null));
        Assert.Equal(["In Default", "In Mine", "Unlocated"], Names([Site.DefaultSiteId, mine.Id]));
        Assert.Equal(["In Hers"], Names([hers.Id]));
        Assert.Empty(Names([]));
    }

    [Fact]
    public void LocationOverviews_AreScopedToReadableSites_WithWriteFlag()
    {
        var user = AddUser("kid");
        var mine = _sites.Create("Mine", null);
        var hers = _sites.Create("Hers", null);
        _sites.SetGrants(mine.Id, [(SitePrincipalType.User, user, SiteAccessLevel.Read)]);

        LocationTileSummary Tile(string name, int siteId) => new()
        {
            Container = new StorageContainer { Id = AddLocation(name, siteId), Name = name, SiteId = siteId },
        };
        var tiles = new[] { Tile("D", Site.DefaultSiteId), Tile("M", mine.Id), Tile("H", hers.Id) };

        var all = LocationsController.ToSiteScopedDtos(tiles, _access.Get(user), _sites.GetAll(), null);
        Assert.Equal(["D", "M"], all.Select(d => d.Name));
        Assert.True(all.Single(d => d.Name == "D").CanWrite);
        var m = all.Single(d => d.Name == "M");
        Assert.False(m.CanWrite);
        Assert.Equal("Mine", m.SiteName);

        var onlyMine = LocationsController.ToSiteScopedDtos(tiles, _access.Get(user), _sites.GetAll(), mine.Id);
        Assert.Equal(["M"], onlyMine.Select(d => d.Name));

        // A site the user can't read yields nothing even when asked for explicitly.
        Assert.Empty(LocationsController.ToSiteScopedDtos(tiles, _access.Get(user), _sites.GetAll(), hers.Id));
    }

    // --- request-level checks ------------------------------------------------------------------

    [Fact]
    public void RequestSiteAccess_ChecksLocationsAndLots()
    {
        var user = AddUser("kid");
        var mine = _sites.Create("Mine", null);
        var hers = _sites.Create("Hers", null);
        _sites.SetGrants(mine.Id, [(SitePrincipalType.User, user, SiteAccessLevel.Read)]);
        var myBox = AddLocation("My Box", mine.Id);
        var herBox = AddLocation("Her Box", hers.Id);
        var myLot = AddLot("Mine card", myBox);
        var herLot = AddLot("Her card", herBox);
        var looseLot = AddLot("Loose", null);

        var req = RequestFor(user);
        Assert.True(req.CanReadLocation(myBox));
        Assert.False(req.CanWriteLocation(myBox));
        Assert.False(req.CanReadLocation(herBox));
        Assert.True(req.CanWriteLots([looseLot]));      // unlocated = default site
        Assert.True(req.CanReadLots([myLot, looseLot]));
        Assert.False(req.CanReadLots([myLot, herLot]));
        Assert.True(req.CanReadLocation(999_999));       // unknown → let the endpoint 404
    }

    [Fact]
    public void Check_ReturnsNotFoundForHidden_ForbiddenForReadOnly()
    {
        var user = AddUser("kid");
        var ro = _sites.Create("ReadOnly", null);
        var hidden = _sites.Create("Hidden", null);
        _sites.SetGrants(ro.Id, [(SitePrincipalType.User, user, SiteAccessLevel.Read)]);
        var access = _access.Get(user);

        Assert.Null(RequireSiteAccessAttribute.Check(access, [ro.Id], SiteAccessLevel.Read));
        var forbidden = Assert.IsType<ObjectResult>(RequireSiteAccessAttribute.Check(access, [ro.Id], SiteAccessLevel.Write));
        Assert.Equal(403, forbidden.StatusCode);
        Assert.IsType<NotFoundObjectResult>(RequireSiteAccessAttribute.Check(access, [hidden.Id], SiteAccessLevel.Read));
    }

    private sealed record Body(int ContainerId, List<int> Ids);

    [Fact]
    public void ResolveIds_ReadsArgumentsAndBodyProperties()
    {
        var args = new Dictionary<string, object?>
        {
            ["id"] = 5,
            ["r"] = new Body(9, [1, 2, 3]),
        };
        Assert.Equal([5], RequireSiteAccessAttribute.ResolveIds(args, "id"));
        Assert.Equal([9], RequireSiteAccessAttribute.ResolveIds(args, "ContainerId"));
        Assert.Equal([1, 2, 3], RequireSiteAccessAttribute.ResolveIds(args, "Ids"));
        Assert.Equal([5, 9], RequireSiteAccessAttribute.ResolveIds(args, "id, containerId"));
        Assert.Empty(RequireSiteAccessAttribute.ResolveIds(args, "missing"));
    }

    // --- MCP ----------------------------------------------------------------------------------

    [Fact]
    public void Mcp_ResolveSite_AcceptsIdOrName()
    {
        var home = _sites.Create("Andrew's House", null);
        Assert.Null(OmniCard.Web.Mcp.Tools.McpSiteParsing.ResolveSite(_sites, null));
        Assert.Equal(home.Id, OmniCard.Web.Mcp.Tools.McpSiteParsing.ResolveSite(_sites, home.Id.ToString()));
        Assert.Equal(home.Id, OmniCard.Web.Mcp.Tools.McpSiteParsing.ResolveSite(_sites, "andrew's house"));
        Assert.Throws<ArgumentException>(() => OmniCard.Web.Mcp.Tools.McpSiteParsing.ResolveSite(_sites, "Nowhere"));

        var listed = new OmniCard.Web.Mcp.Tools.SiteTools(_sites).ListSites();
        Assert.Equal([Site.DefaultSiteName, "Andrew's House"], listed.Select(s => s.Name));
    }

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
