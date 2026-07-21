using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class ReceiptServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly string _dataDir;

    public ReceiptServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Database.EnsureCreated();

        _dataDir = Path.Combine(Path.GetTempPath(), "omnicard-receipt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose() { _conn.Dispose(); if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, true); }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> o) : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }

    private sealed class DataPathStub(string dir) : OmniCard.Interfaces.IDataPathService
    {
        public string DataDirectory => dir;
        public string ScansDirectory => dir;
        public string TempScansDirectory => dir;
        public string SymbolsCacheDirectory => dir;
        public string LogsDirectory => dir;
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }

    private ReceiptService BuildService(out int orderId)
    {
        // Seed customer, product, lot, order + line via the real services.
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Customers.Add(new Customer { Id = 1, Name = "Ada Lovelace", AddressLine1 = "12 Analytical Way", City = "London", PostalCode = "EC1" });
            var p = new Product { Id = 1, Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Sol Ring", SetName = "Commander", Foil = true };
            ctx.Products.Add(p);
            ctx.SaveChanges();
            ctx.Lots.Add(new InventoryLot { Id = 1, ProductId = 1, Quantity = 2, Condition = "NM", UnitCost = 1m });
            ctx.SaveChanges();
        }

        var settings = new SalesSettingsService(new DataPathStub(_dataDir));
        settings.SaveCompany(new CompanyProfile { Name = "Acme Cards", AddressLine1 = "1 Main", City = "Reno", State = "NV", PostalCode = "89501" });
        settings.SaveReceipt(new ReceiptSettings { WidthMm = 80, ShowPrices = true, FooterText = "Thank you!" });

        var orders = new OrderService(new Factory(_opts), new ListingService(new Factory(_opts), settings));
        var customers = new CustomerService(new Factory(_opts));
        var order = orders.CreateOrder(1, SalesChannel.TcgPlayer, "TCG-100");
        orders.AddLine(order.Id, 1, 3.50m);
        order.ShippingChargedToBuyer = 1.00m;
        orders.UpdateOrder(order);
        orderId = order.Id;

        return new ReceiptService(orders, customers, settings, new DataPathStub(_dataDir));
    }

    [Fact]
    public void BuildReceipt_AssemblesLines_Totals_AndBlocks()
    {
        var svc = BuildService(out var orderId);

        var doc = svc.BuildReceipt(orderId);

        Assert.Equal("Acme Cards", doc.CompanyName);
        Assert.Contains("Reno", doc.CompanyAddressBlock);
        Assert.Equal("Ada Lovelace", doc.CustomerName);
        Assert.Contains("London", doc.CustomerAddressBlock);
        Assert.Equal("TCG-100", doc.OrderNumber);

        var line = Assert.Single(doc.Lines);
        Assert.Equal("Sol Ring", line.Name);
        Assert.Equal("Commander", line.Set);
        Assert.Equal("NM", line.Condition);
        Assert.True(line.IsFoil);
        Assert.Equal(1, line.Quantity);
        Assert.Equal(3.50m, line.UnitSalePrice);
        Assert.Equal(3.50m, line.LineTotal);

        Assert.True(doc.ShowPrices);
        Assert.Equal(3.50m, doc.ItemsTotal);
        Assert.Equal(1.00m, doc.Shipping);
        Assert.Equal(4.50m, doc.GrandTotal);
        Assert.Equal("Thank you!", doc.FooterText);
        Assert.Equal(80, doc.WidthMm);
        Assert.Null(doc.CompanyLogoAbsolutePath);   // no logo set
    }

    [Fact]
    public void BuildReceipt_UnknownOrder_Throws()
    {
        var svc = BuildService(out _);
        Assert.Throws<InvalidOperationException>(() => svc.BuildReceipt(99999));
    }
}
