using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>
/// Covers password hashing and the user-account service (seeding, username/email auth, create/delete,
/// setup-key first sign-in, admin-required resets, and self-service password change). Uses the same in-memory SQLite pattern as the other web tests.
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

    // Creates an account and redeems its setup key, as the user would on first sign-in.
    private async Task<OmniCard.Shared.Settings.User> CreateWithPasswordAsync(string username, string password, string? email = null)
    {
        var u = await _users.CreateAsync(username, "SETUP1234", email: email);
        Assert.Equal(SetupKeyResult.Ok, (await _users.CompleteSetupAsync(username, "SETUP1234", password)).Result);
        return u;
    }

    [Fact]
    public async Task Create_RequiresSetupKey_Then_Authenticate_And_RejectsDuplicate()
    {
        var u = await _users.CreateAsync("alice", "Setup1234", isAdmin: false);
        Assert.True(u.Id > 0);
        Assert.False(u.IsSystem);
        Assert.Equal(SignInStep.SetupKey, await _users.GetSignInStepAsync("alice"));
        Assert.Null(await _users.AuthenticateAsync("alice", "Setup1234")); // the key isn't a password

        // Keys are case-insensitive so they can be read aloud.
        var outcome = await _users.CompleteSetupAsync("alice", "setup1234", "pw-1234");
        Assert.Equal(SetupKeyResult.Ok, outcome.Result);
        Assert.Equal(SignInStep.Password, await _users.GetSignInStepAsync("alice"));
        Assert.NotNull(await _users.AuthenticateAsync("alice", "pw-1234"));

        // A used key can't be redeemed again.
        Assert.Equal(SetupKeyResult.NotPending, (await _users.CompleteSetupAsync("alice", "SETUP1234", "x")).Result);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _users.CreateAsync("alice", "OTHER1234", false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc12")]          // too short
    [InlineData("has space1")]
    [InlineData("symbol!123")]
    public async Task Create_Rejects_InvalidSetupKey(string key) =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => _users.CreateAsync("erin", key));

    [Fact]
    public async Task Delete_Blocks_SystemAccount_ButAllowsOthers()
    {
        _users.EnsureSeeded();
        var admin = (await _users.ListAsync()).Single();
        Assert.False(await _users.DeleteAsync(admin.Id)); // system account protected

        var bob = await _users.CreateAsync("bob", "SETUP1234");
        Assert.True(await _users.DeleteAsync(bob.Id));
        Assert.Null(await _users.FindByIdAsync(bob.Id));
    }

    // --- Email sign-in ---

    [Fact]
    public async Task Email_IsNormalized_Unique_And_WorksAsLogin()
    {
        await CreateWithPasswordAsync("frank", "pw-frank", email: "  Frank@Example.COM ");
        Assert.Equal("frank@example.com", (await _users.ListAsync()).Single().Email);

        Assert.NotNull(await _users.AuthenticateAsync("FRANK@example.com", "pw-frank"));
        Assert.NotNull(await _users.AuthenticateAsync("frank", "pw-frank"));
        Assert.Null(await _users.AuthenticateAsync("frank@example.com", "wrong"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _users.CreateAsync("frank2", "SETUP1234", email: "frank@example.com"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _users.CreateAsync("gina", "SETUP1234", email: "not-an-email"));
    }

    [Fact]
    public async Task UpdateUser_SetsAndClearsEmail_RejectsTaken()
    {
        var a = await _users.CreateAsync("hank", "SETUP1234", email: "hank@example.com");
        var b = await _users.CreateAsync("ivy", "SETUP1234");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _users.UpdateUserAsync(b.Id, b.RoleId, b.Overrides, false, "hank@example.com"));
        Assert.Equal("ivy@example.com", (await _users.UpdateUserAsync(b.Id, b.RoleId, b.Overrides, false, "Ivy@example.com"))!.Email);
        Assert.Null((await _users.UpdateUserAsync(a.Id, a.RoleId, a.Overrides, false, "  "))!.Email);
    }

    [Fact]
    public async Task SignInStep_ReportsPassword_ForUnknownLogin()
    {
        Assert.Equal(SignInStep.Password, await _users.GetSignInStepAsync("nobody"));
        Assert.Equal(SignInStep.Password, await _users.GetSignInStepAsync("nobody@example.com"));
        Assert.Equal(SetupKeyResult.InvalidKey, (await _users.CompleteSetupAsync("nobody", "SETUP1234", "x")).Result);
    }

    // --- Password change / required reset ---

    [Fact]
    public async Task ChangePassword_RequiresCurrent()
    {
        var u = await CreateWithPasswordAsync("carol", "old-pass");

        Assert.False(await _users.ChangePasswordAsync(u.Id, "wrong-current", "new-pass"));
        Assert.NotNull(await _users.AuthenticateAsync("carol", "old-pass")); // unchanged

        Assert.True(await _users.ChangePasswordAsync(u.Id, "old-pass", "new-pass"));
        Assert.Null(await _users.AuthenticateAsync("carol", "old-pass"));
        Assert.NotNull(await _users.AuthenticateAsync("carol", "new-pass"));
    }

    [Fact]
    public async Task RequirePasswordReset_VoidsPassword_UntilKeyRedeemed()
    {
        var u = await CreateWithPasswordAsync("dave", "old-pass");
        Assert.True(await _users.RequirePasswordResetAsync(u.Id, "RESET5678"));

        Assert.Null(await _users.AuthenticateAsync("dave", "old-pass")); // old password stops working at once
        Assert.Equal(SignInStep.SetupKey, await _users.GetSignInStepAsync("dave"));
        Assert.True((await _users.FindByIdAsync(u.Id))!.SetupKeyHash is not null);

        Assert.Equal(SetupKeyResult.Ok, (await _users.CompleteSetupAsync("dave", "RESET5678", "new-pass")).Result);
        Assert.NotNull(await _users.AuthenticateAsync("dave", "new-pass"));
        Assert.Null((await _users.FindByIdAsync(u.Id))!.SetupKeyHash);
    }

    [Fact]
    public async Task RequirePasswordReset_Rejects_InvalidKey_OrUnknownUser()
    {
        var u = await CreateWithPasswordAsync("jill", "pw");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _users.RequirePasswordResetAsync(u.Id, "x"));
        Assert.NotNull(await _users.AuthenticateAsync("jill", "pw")); // untouched by the rejected request
        Assert.False(await _users.RequirePasswordResetAsync(9999, "RESET5678"));
    }

    [Fact]
    public async Task WrongSetupKeys_CountDown_ThenLockUntilReissued()
    {
        var u = await _users.CreateAsync("kim", "SETUP1234");

        for (var i = 1; i < SetupKeys.MaxFailedAttempts; i++)
        {
            var miss = await _users.CompleteSetupAsync("kim", "WRONG0000", "pw");
            Assert.Equal(SetupKeyResult.InvalidKey, miss.Result);
            Assert.Equal(SetupKeys.MaxFailedAttempts - i, miss.AttemptsLeft);
        }
        Assert.Equal(SetupKeyResult.Locked, (await _users.CompleteSetupAsync("kim", "WRONG0000", "pw")).Result);

        // The right key no longer works: the account is locked until an admin issues a new one.
        Assert.Equal(SignInStep.Locked, await _users.GetSignInStepAsync("kim"));
        Assert.Equal(SetupKeyResult.Locked, (await _users.CompleteSetupAsync("kim", "SETUP1234", "pw")).Result);

        Assert.True(await _users.RequirePasswordResetAsync(u.Id, "AGAIN2345"));
        Assert.Equal(SetupKeyResult.Ok, (await _users.CompleteSetupAsync("kim", "AGAIN2345", "pw")).Result);
    }

    [Fact]
    public async Task CompleteSetup_BlankPassword_Throws_WithoutCostingAnAttempt()
    {
        var u = await _users.CreateAsync("lou", "SETUP1234");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _users.CompleteSetupAsync("lou", "WRONG0000", ""));
        Assert.Equal(0, (await _users.FindByIdAsync(u.Id))!.SetupKeyFailedAttempts);
    }

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
