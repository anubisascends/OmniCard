using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Settings;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>
/// Remembered per-user UI settings: each user has their own key → value bag; setting replaces, a blank
/// value forgets, bad keys/oversized values are refused, and a user's settings go with the user.
/// </summary>
public class UserPreferenceServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly UserPreferenceService _prefs;
    private readonly int _alice;
    private readonly int _bob;

    public UserPreferenceServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();
        _prefs = new UserPreferenceService(new Factory(_opts), TimeProvider.System);
        _alice = AddUser("alice");
        _bob = AddUser("bob");
    }

    public void Dispose() => _conn.Dispose();

    private int AddUser(string name)
    {
        using var db = new OmniCardDbContext(_opts);
        var u = new User { Username = name, PasswordHash = "x" };
        db.Users.Add(u);
        db.SaveChanges();
        return u.Id;
    }

    [Fact]
    public async Task Set_StoresPerUser_AndReplaces()
    {
        await _prefs.SetAsync(_alice, "scan.review", """{"compareHeight":600}""");
        await _prefs.SetAsync(_alice, "scan.review", """{"compareHeight":700,"holdEnterMs":250}""");
        await _prefs.SetAsync(_bob, "scan.review", """{"holdEnterMs":1000}""");

        var alice = await _prefs.GetAllAsync(_alice);
        Assert.Equal("""{"compareHeight":700,"holdEnterMs":250}""", Assert.Single(alice).Value);
        Assert.Equal("""{"holdEnterMs":1000}""", (await _prefs.GetAllAsync(_bob))["scan.review"]);
    }

    [Fact]
    public async Task BlankValue_ForgetsTheSetting()
    {
        await _prefs.SetAsync(_alice, "scan.review", "{}");
        await _prefs.SetAsync(_alice, "scan.review", null);
        await _prefs.SetAsync(_alice, "never.set", ""); // Forgetting a missing key is a no-op.

        Assert.Empty(await _prefs.GetAllAsync(_alice));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".starts-with-dot")]
    [InlineData("has space")]
    [InlineData("slash/key")]
    public async Task InvalidKey_IsRefused(string key)
    {
        Assert.False(UserPreferenceService.IsValidKey(key));
        await Assert.ThrowsAsync<ArgumentException>(() => _prefs.SetAsync(_alice, key, "{}"));
    }

    [Fact]
    public async Task OversizedValue_IsRefused()
    {
        var value = new string('x', UserPreference.MaxValueLength + 1);
        await Assert.ThrowsAsync<ArgumentException>(() => _prefs.SetAsync(_alice, "scan.review", value));
    }

    [Fact]
    public async Task DeletingTheUser_RemovesTheirSettings()
    {
        await _prefs.SetAsync(_alice, "scan.review", "{}");
        using (var db = new OmniCardDbContext(_opts))
        {
            db.Users.Remove(db.Users.Single(u => u.Id == _alice));
            db.SaveChanges();
        }

        using var check = new OmniCardDbContext(_opts);
        Assert.Empty(check.UserPreferences);
    }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
