using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class CustomerServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;

    public CustomerServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private CustomerService Svc() => new(new Factory(_opts));
    private sealed class Factory(DbContextOptions<OmniCardDbContext> o) : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }

    [Fact]
    public void Create_Update_Delete_RoundTrip()
    {
        var svc = Svc();
        var created = svc.Create(new Customer { Name = "Ada", Email = "ada@x.com" });
        Assert.True(created.Id > 0);

        created.Email = "ada@y.com";
        svc.Update(created);
        Assert.Equal("ada@y.com", svc.Get(created.Id)!.Email);

        Assert.Single(svc.GetAll());
        svc.Delete(created.Id);
        Assert.Empty(svc.GetAll());
    }
}
