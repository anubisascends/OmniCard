using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Security;
using OmniCard.Shared.Settings;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>
/// Covers effective-permission resolution (role ∪ grant − deny, admin short-circuit), role seeding,
/// and cache invalidation. Same in-memory SQLite pattern as the other web tests.
/// </summary>
public class PermissionServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly MockFactory _factory;
    private readonly UserService _users;
    private readonly PermissionService _perms;

    public PermissionServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(opts)) ctx.Database.EnsureCreated();
        _factory = new MockFactory(opts);
        _users = new UserService(_factory);
        _perms = new PermissionService(_factory);
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public async Task EnsureSeeded_Seeds_SystemRoles()
    {
        _users.EnsureSeeded();
        var roles = await _users.ListRolesAsync();

        Assert.Contains(roles, r => r.Name == UserService.AdministratorRole && r.IsSystem);
        var viewer = Assert.Single(roles, r => r.Name == UserService.ViewerRole);
        Assert.Equal(Permissions.ViewOnly.OrderBy(x => x), viewer.Permissions.OrderBy(x => x));
        Assert.Contains(roles, r => r.Name == UserService.StaffRole);
    }

    [Fact]
    public async Task Admin_And_System_Hold_All_Permissions()
    {
        _users.EnsureSeeded();
        var admin = (await _users.ListAsync()).Single(); // seeded system admin

        var effective = await _perms.GetEffectiveAsync(admin.Id);
        Assert.Equal(Permissions.All.OrderBy(x => x), effective.OrderBy(x => x));
        Assert.True(await _perms.HasPermissionAsync(admin.Id, Permissions.CollectionDelete));
    }

    [Fact]
    public async Task NewUser_Defaults_To_ViewOnly_ViaViewerRole()
    {
        _users.EnsureSeeded();
        var alice = await _users.CreateAsync("alice", "pw-1234"); // no role → defaults to Viewer

        var effective = await _perms.GetEffectiveAsync(alice.Id);
        Assert.Equal(Permissions.ViewOnly.OrderBy(x => x), effective.OrderBy(x => x));
        Assert.True(await _perms.HasPermissionAsync(alice.Id, Permissions.CollectionView));
        Assert.False(await _perms.HasPermissionAsync(alice.Id, Permissions.CollectionEdit));
    }

    [Fact]
    public async Task Overrides_Grant_Adds_And_Deny_Removes()
    {
        _users.EnsureSeeded();
        var bob = await _users.CreateAsync("bob", "pw-1234"); // Viewer baseline (all *.view)

        // Grant an edit permission; deny a view permission the role provides.
        await _users.UpdateUserAsync(bob.Id, bob.RoleId,
            new PermissionOverrides
            {
                Grant = [Permissions.CollectionEdit],
                Deny = [Permissions.CollectionView],
            },
            isAdmin: false);
        _perms.Invalidate(bob.Id);

        Assert.True(await _perms.HasPermissionAsync(bob.Id, Permissions.CollectionEdit));   // granted
        Assert.False(await _perms.HasPermissionAsync(bob.Id, Permissions.CollectionView));  // denied wins
        Assert.True(await _perms.HasPermissionAsync(bob.Id, Permissions.InventoryView));    // other role views intact
    }

    [Fact]
    public async Task Making_User_Admin_Unlocks_Everything_After_Invalidate()
    {
        _users.EnsureSeeded();
        var carol = await _users.CreateAsync("carol", "pw-1234");
        Assert.False(await _perms.HasPermissionAsync(carol.Id, Permissions.EbayManage));

        await _users.UpdateUserAsync(carol.Id, carol.RoleId, carol.Overrides, isAdmin: true);
        _perms.Invalidate(carol.Id); // mirrors what UsersController does

        Assert.True(await _perms.HasPermissionAsync(carol.Id, Permissions.EbayManage));
    }

    [Fact]
    public async Task Unknown_User_Has_No_Permissions()
    {
        var effective = await _perms.GetEffectiveAsync(999999);
        Assert.Empty(effective);
    }

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
