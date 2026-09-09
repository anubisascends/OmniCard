using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>
/// Covers password hashing and the user-account service (seeding, auth, create/delete, and the
/// password change/reset flows). Uses the same in-memory SQLite pattern as the other web tests.
/// </summary>
public class UserServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly MockFactory _factory;
    private readonly UserService _users;

    public UserServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(opts)) ctx.Database.EnsureCreated();
        _factory = new MockFactory(opts);
        _users = new UserService(_factory);
    }

    public void Dispose() => _conn.Dispose();

    // --- PasswordHasher ---

    [Fact]
    public void Hash_IsSalted_AndVerifies()
    {
        var h1 = PasswordHasher.Hash("hunter2");
        var h2 = PasswordHasher.Hash("hunter2");

        Assert.NotEqual(h1, h2);                     // fresh random salt each time
        Assert.DoesNotContain("hunter2", h1);        // never stores the plaintext
        Assert.True(PasswordHasher.Verify("hunter2", h1));
        Assert.True(PasswordHasher.Verify("hunter2", h2));
        Assert.False(PasswordHasher.Verify("wrong", h1));
    }

    [Fact]
    public void Verify_Rejects_EmptyOrGarbage()
    {
        Assert.False(PasswordHasher.Verify("x", null));
        Assert.False(PasswordHasher.Verify("x", ""));
        Assert.False(PasswordHasher.Verify("", PasswordHasher.Hash("x")));
        Assert.False(PasswordHasher.Verify("x", "not-a-valid-encoded-hash"));
    }

    // --- Seeding ---

    [Fact]
    public async Task EnsureSeeded_CreatesAdmin_Once()
    {
        _users.EnsureSeeded();
        _users.EnsureSeeded(); // idempotent

        var all = await _users.ListAsync();
        var admin = Assert.Single(all);
        Assert.Equal(UserService.SystemUsername, admin.Username);
        Assert.True(admin.IsSystem);
        Assert.True(admin.IsAdmin);
        Assert.NotNull(await _users.AuthenticateAsync(UserService.SystemUsername, UserService.SystemDefaultPassword));
    }

    // --- Auth ---

    [Fact]
    public async Task Authenticate_ReturnsNull_OnBadPassword_OrUnknownUser()
    {
        _users.EnsureSeeded();
        Assert.Null(await _users.AuthenticateAsync(UserService.SystemUsername, "wrong"));
        Assert.Null(await _users.AuthenticateAsync("nobody", "admin"));
    }

    // --- Create / duplicate / delete ---

    [Fact]
    public async Task Create_Then_Authenticate_And_RejectsDuplicate()
    {
        var u = await _users.CreateAsync("alice", "pw-1234", isAdmin: false);
        Assert.True(u.Id > 0);
        Assert.False(u.IsSystem);
        Assert.NotNull(await _users.AuthenticateAsync("alice", "pw-1234"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _users.CreateAsync("alice", "other", false));
    }

    [Fact]
    public async Task Delete_Blocks_SystemAccount_ButAllowsOthers()
    {
        _users.EnsureSeeded();
        var admin = (await _users.ListAsync()).Single();
        Assert.False(await _users.DeleteAsync(admin.Id)); // system account protected

        var bob = await _users.CreateAsync("bob", "pw-1234");
        Assert.True(await _users.DeleteAsync(bob.Id));
        Assert.Null(await _users.FindByIdAsync(bob.Id));
    }

    // --- Password change / reset ---

    [Fact]
    public async Task ChangePassword_RequiresCurrent()
    {
        var u = await _users.CreateAsync("carol", "old-pass");

        Assert.False(await _users.ChangePasswordAsync(u.Id, "wrong-current", "new-pass"));
        Assert.NotNull(await _users.AuthenticateAsync("carol", "old-pass")); // unchanged

        Assert.True(await _users.ChangePasswordAsync(u.Id, "old-pass", "new-pass"));
        Assert.Null(await _users.AuthenticateAsync("carol", "old-pass"));
        Assert.NotNull(await _users.AuthenticateAsync("carol", "new-pass"));
    }

    [Fact]
    public async Task ResetPassword_Sets_WithoutCurrent()
    {
        var u = await _users.CreateAsync("dave", "old-pass");
        Assert.True(await _users.ResetPasswordAsync(u.Id, "reset-pass"));
        Assert.NotNull(await _users.AuthenticateAsync("dave", "reset-pass"));
    }

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
