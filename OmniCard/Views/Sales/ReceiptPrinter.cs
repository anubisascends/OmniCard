using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

/// <summary>
/// Prints a <see cref="ReceiptDocument"/> as a WPF <see cref="FlowDocument"/> sized to the
/// configured thermal width. Mirrors <see cref="PickListPrinter"/>. No preview (the PDF export
/// is the preview).
/// </summary>
public static class ReceiptPrinter
{
    private const double DipPerMm = 96.0 / 25.4;

    public static void Print(ReceiptDocument receipt)
    {
        // Phase 3: the user selects the thermal printer in the PrintDialog. (Pre-selecting a
        // saved default printer is a deferred nice-to-have — see the note after this code.)
        var dialog = new PrintDialog();

        var pageWidth = receipt.WidthMm * DipPerMm;
        var padding = receipt.MarginMm * DipPerMm;

        var doc = new FlowDocument
        {
            PageWidth = pageWidth,
            ColumnWidth = double.PositiveInfinity,
            PagePadding = new Thickness(padding),
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

        if (dialog.ShowDialog() != true) return;
        dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Receipt");
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
