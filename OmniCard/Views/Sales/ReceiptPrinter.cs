using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

/// <summary>
/// Prints a <see cref="ReceiptDocument"/> as a WPF <see cref="FlowDocument"/> sized to the
/// configured thermal width, but clamped to the selected printer's printable area so content
/// isn't clipped by the printer's non-printable hardware margins. No preview (the PDF export
/// is the preview).
/// </summary>
public static class ReceiptPrinter
{
    private const double DipPerMm = 96.0 / 25.4;

    /// <summary>A printer's printable area, in DIPs (1/96"): the unprintable top-left offset
    /// (<paramref name="OriginX"/>/<paramref name="OriginY"/>) and the printable size
    /// (<paramref name="ExtentWidth"/>/<paramref name="ExtentHeight"/>).</summary>
    public readonly record struct PrintableArea(double OriginX, double OriginY, double ExtentWidth, double ExtentHeight);

    /// <summary>Computed FlowDocument page sizing for a receipt.</summary>
    public readonly record struct ReceiptPageLayout(double PageWidth, Thickness Padding);

    public static void Print(ReceiptDocument receipt)
    {
        // The user selects the thermal printer in the PrintDialog.
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        var layout = ComputeLayout(
            receipt.WidthMm * DipPerMm,
            receipt.MarginMm * DipPerMm,
            GetPrintableArea(dialog));

        var doc = new FlowDocument
        {
            PageWidth = layout.PageWidth,
            ColumnWidth = double.PositiveInfinity,
            PagePadding = layout.Padding,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = receipt.FontPointSize * 96.0 / 72.0,   // pt → DIP
        };

        // Logo
        if (receipt.CompanyLogoAbsolutePath is not null && File.Exists(receipt.CompanyLogoAbsolutePath))
        {
            var img = new Image
            {
                Source = new BitmapImage(new System.Uri(receipt.CompanyLogoAbsolutePath)),
                MaxHeight = 60,
                Stretch = Stretch.Uniform,
            };
            var logoPara = new Paragraph(new InlineUIContainer(img)) { TextAlignment = TextAlignment.Center, Margin = new Thickness(0) };
            doc.Blocks.Add(logoPara);
        }

        AddCentered(doc, receipt.CompanyName, bold: true, sizeDelta: 3);
        AddCentered(doc, receipt.CompanyAddressBlock);
        AddCentered(doc, receipt.CompanyPhone);
        AddCentered(doc, receipt.CompanyEmail);

        AddSeparator(doc);

        if (!string.IsNullOrWhiteSpace(receipt.OrderNumber)) AddLine(doc, $"Order: {receipt.OrderNumber}", bold: true);
        AddLine(doc, $"Date: {receipt.OrderDate:yyyy-MM-dd}");
        var ship = $"{receipt.Carrier} {receipt.TrackingNumber}".Trim();
        if (!string.IsNullOrWhiteSpace(ship)) AddLine(doc, $"Ship: {ship}");

        AddLine(doc, "Ship to:", bold: true);
        AddLine(doc, receipt.CustomerName);
        if (!string.IsNullOrWhiteSpace(receipt.CustomerAddressBlock)) AddLine(doc, receipt.CustomerAddressBlock);

        AddSeparator(doc);

        // Line items
        foreach (var line in receipt.Lines)
        {
            var label = line.Name
                        + (string.IsNullOrWhiteSpace(line.Set) ? "" : $" ({line.Set})")
                        + (string.IsNullOrWhiteSpace(line.Condition) ? "" : $" {line.Condition}")
                        + (line.IsFoil ? " *foil" : "");
            var text = receipt.ShowPrices
                ? $"{label}  x{line.Quantity}   ${line.LineTotal:N2}"
                : $"{label}  x{line.Quantity}";
            AddLine(doc, text);
        }

        if (receipt.ShowPrices)
        {
            AddSeparator(doc);
            AddLine(doc, $"Items: ${receipt.ItemsTotal:N2}", align: TextAlignment.Right);
            AddLine(doc, $"Shipping: ${receipt.Shipping:N2}", align: TextAlignment.Right);
            AddLine(doc, $"Total: ${receipt.GrandTotal:N2}", bold: true, align: TextAlignment.Right);
        }

        if (!string.IsNullOrWhiteSpace(receipt.FooterText))
            AddCentered(doc, receipt.FooterText);

        dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Receipt");
    }

    /// <summary>Computes the FlowDocument page width + padding so the receipt stays inside the
    /// printer's printable area. Clamps the page width to the printable right edge and insets the
    /// content past the left/top unprintable hardware margins; the configured margin is a floor.
    /// Falls back to the desired width + uniform margin when no printable area is available.</summary>
    public static ReceiptPageLayout ComputeLayout(double desiredWidthDip, double marginDip, PrintableArea? area)
    {
        if (marginDip < 0) marginDip = 0;

        if (area is not PrintableArea a || a.ExtentWidth <= 0)
            return new ReceiptPageLayout(desiredWidthDip, new Thickness(marginDip));

        var printableRight = a.OriginX + a.ExtentWidth;

        // Never let the page extend past the printable right edge (that's what gets clipped),
        // but don't force it wider than the user asked for either.
        var pageWidth = System.Math.Min(desiredWidthDip, printableRight);

        // Inset content past the hardware unprintable borders; keep the configured margin as a floor.
        var left = System.Math.Max(marginDip, a.OriginX);
        var top = System.Math.Max(marginDip, a.OriginY);
        var right = marginDip;
        var bottom = marginDip;

        // Guard against a degenerate printable area collapsing the content column.
        if (left + right >= pageWidth)
        {
            left = System.Math.Min(a.OriginX, pageWidth);
            right = 0;
        }

        return new ReceiptPageLayout(pageWidth, new Thickness(left, top, right, bottom));
    }

    private static PrintableArea? GetPrintableArea(PrintDialog dialog)
    {
        // Best effort: some drivers/virtual printers throw or don't report an imageable area.
        try
        {
            var caps = dialog.PrintQueue?.GetPrintCapabilities(dialog.PrintTicket);
            if (caps?.PageImageableArea is not { } a) return null;
            return new PrintableArea(a.OriginWidth, a.OriginHeight, a.ExtentWidth, a.ExtentHeight);
        }
        catch
        {
            return null;
        }
    }

    private static void AddCentered(FlowDocument doc, string? text, bool bold = false, double sizeDelta = 0)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var run = new Run(text);
        var para = new Paragraph(run) { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
        if (bold) para.FontWeight = FontWeights.Bold;
        if (sizeDelta != 0) para.FontSize = doc.FontSize + sizeDelta;
        doc.Blocks.Add(para);
    }

    private static void AddLine(FlowDocument doc, string? text, bool bold = false, TextAlignment align = TextAlignment.Left)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var para = new Paragraph(new Run(text)) { TextAlignment = align, Margin = new Thickness(0, 0, 0, 2) };
        if (bold) para.FontWeight = FontWeights.Bold;
        doc.Blocks.Add(para);
    }

    private static void AddSeparator(FlowDocument doc)
        => doc.Blocks.Add(new Paragraph(new Run(new string('-', 32))) { Margin = new Thickness(0, 2, 0, 2) });
}
