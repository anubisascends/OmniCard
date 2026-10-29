using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Sales;
using OmniCard.Web.Services.Intake;

namespace OmniCard.Tests.Web.Intake;

/// <summary>The store-credit ledger: manual credits/debits with a reason, and no overdraw.</summary>
public class StoreCreditServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly StoreCreditService _credit;
    private readonly int _ada;
    private readonly int _bob;

    public StoreCreditServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var db = new OmniCardDbContext(opts);
        db.Database.EnsureCreated();
        var ada = new Customer { Name = "Ada" };
        var bob = new Customer { Name = "Bob" };
        db.Customers.AddRange(ada, bob);
        db.SaveChanges();
        (_ada, _bob) = (ada.Id, bob.Id);
        _credit = new StoreCreditService(new Factory(opts));
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public void Credits_And_Debits_Sum_To_The_Balance()
    {
        Assert.Equal(25m, _credit.Adjust(_ada, 25m, "Goodwill", 1, "admin"));
        Assert.Equal(15m, _credit.Adjust(_ada, -10m, "Spent at counter", 1, "admin"));

        Assert.Equal(15m, _credit.Balance(_ada));
        var entries = _credit.Entries(_ada);
        Assert.Equal(2, entries.Count);
        Assert.Equal("Spent at counter", entries[0].Reason);
        Assert.Equal("admin", entries[0].CreatedByName);
    }

    [Fact]
    public void Debit_Beyond_The_Balance_Is_Refused()
    {
        _credit.Adjust(_ada, 5m, "Goodwill", null, "admin");

        var ex = Assert.Throws<StoreCreditException>(() => _credit.Adjust(_ada, -5.01m, "Too much", null, "admin"));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(5m, _credit.Balance(_ada));
    }

    [Theory]
    [InlineData(0, "Reason")]
    [InlineData(5, "  ")]
    public void Zero_Amount_Or_Blank_Reason_Is_Refused(decimal amount, string reason)
    {
        var ex = Assert.Throws<StoreCreditException>(() => _credit.Adjust(_ada, amount, reason, null, "admin"));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void Unknown_Customer_Is_Not_Found()
    {
        var ex = Assert.Throws<StoreCreditException>(() => _credit.Adjust(9999, 5m, "Reason", null, "admin"));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public void Balances_Lists_Only_Customers_With_Credit()
    {
        _credit.Adjust(_ada, 12.5m, "Goodwill", null, "admin");
        _credit.Adjust(_bob, 3m, "Goodwill", null, "admin");
        _credit.Adjust(_bob, -3m, "Spent", null, "admin");

        var balances = _credit.Balances();

        Assert.Equal(12.5m, balances[_ada]);
        Assert.False(balances.ContainsKey(_bob));
    }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
