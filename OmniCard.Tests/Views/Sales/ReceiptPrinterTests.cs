using OmniCard.Views.Sales;
using Xunit;

namespace OmniCard.Tests.Views.Sales;

public class ReceiptPrinterTests
{
    [Fact]
    public void ComputeLayout_NoPrintableArea_UsesDesiredWidthAndUniformMargin()
    {
        var layout = ReceiptPrinter.ComputeLayout(desiredWidthDip: 302, marginDip: 15, area: null);

        Assert.Equal(302, layout.PageWidth);
        Assert.Equal(15, layout.Padding.Left);
        Assert.Equal(15, layout.Padding.Top);
        Assert.Equal(15, layout.Padding.Right);
        Assert.Equal(15, layout.Padding.Bottom);
    }

    [Fact]
    public void ComputeLayout_ClampsPageWidth_ToPrintableRightEdge_WhenDesiredExceedsIt()
    {
        // 80mm paper (~302 dip) but the printer can only print [11.3, 283.3] dip.
        var area = new ReceiptPrinter.PrintableArea(OriginX: 11.3, OriginY: 11.3, ExtentWidth: 272, ExtentHeight: 2000);

        var layout = ReceiptPrinter.ComputeLayout(desiredWidthDip: 302, marginDip: 15, area);

        // Page no longer extends past the printable right edge → nothing clipped.
        Assert.Equal(283.3, layout.PageWidth, precision: 3);
        Assert.True(layout.PageWidth - layout.Padding.Right <= 11.3 + 272);
    }

    [Fact]
    public void ComputeLayout_DoesNotWidenBeyondDesired_WhenPrintableAreaIsLarger()
    {
        // Narrow receipt on a printer with a large printable area.
        var area = new ReceiptPrinter.PrintableArea(OriginX: 6, OriginY: 6, ExtentWidth: 800, ExtentHeight: 2000);

        var layout = ReceiptPrinter.ComputeLayout(desiredWidthDip: 220, marginDip: 10, area);

        Assert.Equal(220, layout.PageWidth);
    }

    [Fact]
    public void ComputeLayout_InsetsContentPastHardwareMargins_WhenOriginExceedsConfiguredMargin()
    {
        var area = new ReceiptPrinter.PrintableArea(OriginX: 20, OriginY: 18, ExtentWidth: 400, ExtentHeight: 2000);

        var layout = ReceiptPrinter.ComputeLayout(desiredWidthDip: 302, marginDip: 5, area);

        Assert.Equal(20, layout.Padding.Left);   // clears the 20-dip left hardware margin
        Assert.Equal(18, layout.Padding.Top);     // clears the 18-dip top hardware margin
    }

    [Fact]
    public void ComputeLayout_KeepsConfiguredMargin_AsFloor_WhenHardwareMarginIsSmaller()
    {
        var area = new ReceiptPrinter.PrintableArea(OriginX: 4, OriginY: 4, ExtentWidth: 400, ExtentHeight: 2000);

        var layout = ReceiptPrinter.ComputeLayout(desiredWidthDip: 302, marginDip: 15, area);

        Assert.Equal(15, layout.Padding.Left);   // configured 15-dip margin wins over the 4-dip hardware margin
        Assert.Equal(15, layout.Padding.Top);
    }
}
