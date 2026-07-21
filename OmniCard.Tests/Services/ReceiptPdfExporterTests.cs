using System.IO;
using OmniCard.Audit;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class ReceiptPdfExporterTests : IDisposable
{
    private readonly string _tempDir;

    public ReceiptPdfExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardReceipt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }

    private static ReceiptDocument Sample(bool showPrices) => new()
    {
        CompanyName = "Acme Cards",
        CompanyAddressBlock = "1 Main\nReno NV 89501",
        OrderNumber = "TCG-100",
        OrderDate = new DateTime(2026, 7, 21),
        TrackingNumber = "1Z999",
        Carrier = "UPS",
        CustomerName = "Ada Lovelace",
        CustomerAddressBlock = "12 Analytical Way\nLondon EC1",
        Lines =
        [
            new ReceiptLine { Name = "Sol Ring", Set = "Commander", Condition = "NM", IsFoil = true, Quantity = 1, UnitSalePrice = 3.50m, LineTotal = 3.50m },
            new ReceiptLine { Name = "Counterspell", Set = "MH2", Condition = "LP", IsFoil = false, Quantity = 2, UnitSalePrice = 1.00m, LineTotal = 2.00m },
        ],
        ShowPrices = showPrices,
        ItemsTotal = 5.50m,
        Shipping = 1.00m,
        GrandTotal = 6.50m,
        FooterText = "Thank you!",
        WidthMm = 80,
        MarginMm = 4,
        FontPointSize = 9,
    };

    [Fact]
    public void Export_WritesValidPdf_WithPrices()
    {
        var path = Path.Combine(_tempDir, "receipt.pdf");
        new ReceiptPdfExporter().Export(Sample(showPrices: true), path);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 100);
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    [Fact]
    public void Export_WritesValidPdf_WithoutPrices()
    {
        var path = Path.Combine(_tempDir, "receipt_noprice.pdf");
        new ReceiptPdfExporter().Export(Sample(showPrices: false), path);
        Assert.True(File.Exists(path));
    }
}
