using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class TcgPlayerOrderImportServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly string _dir;

    public TcgPlayerOrderImportServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Database.EnsureCreated();
        _dir = Path.Combine(Path.GetTempPath(), "omnicard-tcgimport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { _conn.Dispose(); if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> o) : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }

    private const string Header =
        "Order #,FirstName,LastName,Address1,Address2,City,State,PostalCode,Country,Order Date,Product Weight,Shipping Method,Item Count,Value Of Products,Shipping Fee Paid,Tracking #,Carrier";

    private string WriteCsv(params string[] dataRows)
    {
        var path = Path.Combine(_dir, "orders-" + Guid.NewGuid().ToString("N") + ".csv");
        File.WriteAllLines(path, new[] { Header }.Concat(dataRows));
        return path;
    }

    private static string Row(string orderNo, string first, string last, string postal,
        string date = "2026-07-17", string items = "8", string value = "320.00", string ship = "19.99")
        => $"\"{orderNo}\",\"{first}\",\"{last}\",\"11323 174th Ave\",\"\",\"Bonney Lake\",\"WA\",\"{postal}\",\"US\",\"{date}\",\"0.56\",\"Standard (7-10 days)\",\"{items}\",\"{value}\",\"{ship}\",\"\",\"\"";

    private TcgPlayerOrderImportService Svc() => new(new Factory(_opts));

    [Fact]
    public void PreviewImport_ParsesFields_AndFlagsNewCustomer()
    {
        var path = WriteCsv(Row("BF5A9364-382FDC-D66E4", "Tad", "Cutright", "98391-8194"));
        var preview = Svc().PreviewImport(path);

        var row = Assert.Single(preview.Rows);
        Assert.Equal("BF5A9364-382FDC-D66E4", row.OrderNumber);
        Assert.Equal("Tad Cutright", row.CustomerName);
        Assert.Equal("Bonney Lake", row.City);
        Assert.Equal("98391-8194", row.PostalCode);
        Assert.Equal(new DateTime(2026, 7, 17), row.OrderDate);
        Assert.Equal(8, row.ItemCount);
        Assert.Equal(320.00m, row.ValueOfProducts);
        Assert.Equal(19.99m, row.ShippingFeePaid);
        Assert.True(row.IsNewCustomer);
        Assert.False(row.IsDuplicateOrder);
        Assert.True(row.Include);
    }

    [Fact]
    public void PreviewImport_MatchesExistingCustomer_OnNameAndPostal()
    {
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Customers.Add(new Customer { Name = "Tad Cutright", PostalCode = "98391-8194" });
            ctx.SaveChanges();
        }
        var preview = Svc().PreviewImport(WriteCsv(Row("ORD-1", "Tad", "Cutright", "98391-8194")));
        var row = Assert.Single(preview.Rows);
        Assert.False(row.IsNewCustomer);
        Assert.NotNull(row.MatchedCustomerId);
    }

    [Fact]
    public void PreviewImport_FlagsDuplicateOrder_AndDefaultsIncludeFalse()
    {
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Customers.Add(new Customer { Id = 5, Name = "X" });
            ctx.Orders.Add(new Order { CustomerId = 5, OrderNumber = "ORD-DUP", OrderDate = DateTime.UtcNow, Status = OrderStatus.Open });
            ctx.SaveChanges();
        }
        var preview = Svc().PreviewImport(WriteCsv(Row("ORD-DUP", "Tad", "Cutright", "98391")));
        var row = Assert.Single(preview.Rows);
        Assert.True(row.IsDuplicateOrder);
        Assert.False(row.Include);
        Assert.False(row.CanInclude);
    }

    [Fact]
    public void Commit_CreatesCustomerAndOrder_ThenIsIdempotent()
    {
        var svc = Svc();
        var preview = svc.PreviewImport(WriteCsv(Row("ORD-100", "Tad", "Cutright", "98391-8194")));

        Assert.Equal(1, svc.Commit(preview));

        using (var ctx = new OmniCardDbContext(_opts))
        {
            var order = ctx.Orders.Single(o => o.OrderNumber == "ORD-100");
            Assert.Equal(SalesChannel.TcgPlayer, order.Channel);
            Assert.Equal(OrderStatus.Open, order.Status);
            Assert.Equal(19.99m, order.ShippingChargedToBuyer);
            Assert.Equal(8, order.ImportedItemCount);
            Assert.Equal(320.00m, order.ImportedProductValue);
            var customer = ctx.Customers.Single(c => c.Id == order.CustomerId);
            Assert.Equal("Tad Cutright", customer.Name);
            Assert.Equal("98391-8194", customer.PostalCode);
        }

        // Re-committing the same preview creates nothing (order number now exists).
        Assert.Equal(0, svc.Commit(preview));
        using (var ctx = new OmniCardDbContext(_opts))
            Assert.Single(ctx.Orders.Where(o => o.OrderNumber == "ORD-100"));
    }

    [Fact]
    public void Commit_RepeatBuyerInSameFile_ReusesOneCustomer()
    {
        var svc = Svc();
        var preview = svc.PreviewImport(WriteCsv(
            Row("ORD-A", "Tad", "Cutright", "98391-8194"),
            Row("ORD-B", "Tad", "Cutright", "98391-8194")));

        Assert.Equal(2, svc.Commit(preview));

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Single(ctx.Customers.Where(c => c.Name == "Tad Cutright"));
        Assert.Equal(2, ctx.Orders.Count(o => o.OrderNumber == "ORD-A" || o.OrderNumber == "ORD-B"));
    }

    [Fact]
    public void PreviewImport_WrongHeader_ReturnsNoRows_AndWarns()
    {
        var path = Path.Combine(_dir, "wrong-" + Guid.NewGuid().ToString("N") + ".csv");
        File.WriteAllLines(path, new[]
        {
            "Name,Set Name,Number,Condition",
            "\"Black Lotus\",\"Alpha\",\"1\",\"Near Mint\"",
        });

        var preview = Svc().PreviewImport(path);

        Assert.Empty(preview.Rows);
        Assert.NotEmpty(preview.Warnings);
        Assert.Contains(preview.Warnings, w => w.Contains("Order #", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PreviewImport_UnparseableOrderDate_KeepsRow_AndWarns()
    {
        var path = WriteCsv(Row("ORD-BADDATE", "Tad", "Cutright", "98391-8194", date: "not-a-date"));
        var preview = Svc().PreviewImport(path);

        var row = Assert.Single(preview.Rows);
        Assert.Equal(DateTime.UtcNow.Date, row.OrderDate);
        Assert.Contains(preview.Warnings, w => w.Contains("ORD-BADDATE", StringComparison.OrdinalIgnoreCase));
    }
}
