using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection.ImportExport;
using OmniCard.Data;
using OmniCard.Shared.ImportExport;
using OmniCard.Shared.Sales;

namespace OmniCard.Tests.Services.ImportExport;

public class OrderCsvImportServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly string _dir;

    public OrderCsvImportServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Database.EnsureCreated();
        _dir = Path.Combine(Path.GetTempPath(), "omnicard-orderimport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { _conn.Dispose(); if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> o) : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }

    private OrderCsvImportService Svc() => new(new Factory(_opts));

    private string WriteCsv(params string[] lines)
    {
        var path = Path.Combine(_dir, "csv-" + Guid.NewGuid().ToString("N") + ".csv");
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void ReadHeaders_ReturnsColumnsInFileOrder()
    {
        var path = WriteCsv("Ref,Buyer,Zip,Total", "\"A-1\",\"Jane Doe\",\"90210\",\"12.00\"");
        Assert.Equal(new[] { "Ref", "Buyer", "Zip", "Total" }, Svc().ReadHeaders(path));
    }

    [Fact]
    public void PreviewImport_CustomMapping_WithFullNameColumn()
    {
        var template = new OrderImportTemplate
        {
            Channel = SalesChannel.Manual,
            ColumnMappings = new()
            {
                [nameof(OrderImportField.OrderNumber)] = "Ref",
                [nameof(OrderImportField.FullName)] = "Buyer",
                [nameof(OrderImportField.PostalCode)] = "Zip",
                [nameof(OrderImportField.ValueOfProducts)] = "Total",
                [nameof(OrderImportField.OrderDate)] = "When",
            },
        };
        var path = WriteCsv(
            "Ref,Buyer,Zip,Total,When",
            "\"A-1\",\"Jane Doe\",\"90210\",\"12.50\",\"2026-05-01\"");

        var preview = Svc().PreviewImport(path, template);

        var row = Assert.Single(preview.Rows);
        Assert.Equal("A-1", row.OrderNumber);
        Assert.Equal("Jane Doe", row.CustomerName);
        Assert.Equal("90210", row.PostalCode);
        Assert.Equal(12.50m, row.ValueOfProducts);
        Assert.Equal(new DateTime(2026, 5, 1), row.OrderDate);
        Assert.True(row.IsNewCustomer);
        Assert.Empty(preview.Warnings);
    }

    [Fact]
    public void PreviewImport_TemplateWithoutOrderNumberMapping_Warns()
    {
        var template = new OrderImportTemplate
        {
            ColumnMappings = new() { [nameof(OrderImportField.FullName)] = "Buyer" },
        };
        var path = WriteCsv("Ref,Buyer", "\"A-1\",\"Jane Doe\"");

        var preview = Svc().PreviewImport(path, template);

        Assert.Empty(preview.Rows);
        Assert.Contains(preview.Warnings, w => w.Contains("Order Number", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PreviewImport_MappedColumnMissingFromFile_Warns()
    {
        var template = OrderImportTemplate.TcgPlayerDefault(); // maps OrderNumber -> "Order #"
        var path = WriteCsv("Name,Set Name,Number", "\"Black Lotus\",\"Alpha\",\"1\"");

        var preview = Svc().PreviewImport(path, template);

        Assert.Empty(preview.Rows);
        Assert.Contains(preview.Warnings, w => w.Contains("Order #", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Commit_UsesGivenChannel_AndStoresAggregates()
    {
        var template = new OrderImportTemplate
        {
            ColumnMappings = new()
            {
                [nameof(OrderImportField.OrderNumber)] = "Ref",
                [nameof(OrderImportField.FullName)] = "Buyer",
                [nameof(OrderImportField.ItemCount)] = "Qty",
                [nameof(OrderImportField.ValueOfProducts)] = "Total",
            },
        };
        var svc = Svc();
        var preview = svc.PreviewImport(
            WriteCsv("Ref,Buyer,Qty,Total", "\"E-9\",\"Sam Roe\",\"3\",\"45.00\""),
            template);

        Assert.Equal(1, svc.Commit(preview, SalesChannel.Ebay));

        using var ctx = new OmniCardDbContext(_opts);
        var order = ctx.Orders.Single(o => o.OrderNumber == "E-9");
        Assert.Equal(SalesChannel.Ebay, order.Channel);
        Assert.Equal(3, order.ImportedItemCount);
        Assert.Equal(45.00m, order.ImportedProductValue);
    }
}
