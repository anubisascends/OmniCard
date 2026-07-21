using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Audit;

public sealed class ReceiptPdfExporter : IReceiptPdfExporter
{
    public void Export(ReceiptDocument doc, string filePath)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                // Continuous roll sized to the configured thermal width.
                page.ContinuousSize((float)doc.WidthMm, Unit.Millimetre);
                page.Margin((float)doc.MarginMm, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize((float)doc.FontPointSize));

                page.Content().Column(col =>
                {
                    // Header: logo + company
                    if (doc.CompanyLogoAbsolutePath is not null && File.Exists(doc.CompanyLogoAbsolutePath))
                        col.Item().AlignCenter().MaxHeight(60).Image(doc.CompanyLogoAbsolutePath).FitHeight();

                    if (!string.IsNullOrWhiteSpace(doc.CompanyName))
                        col.Item().AlignCenter().Text(doc.CompanyName).Bold().FontSize((float)doc.FontPointSize + 3);
                    if (!string.IsNullOrWhiteSpace(doc.CompanyAddressBlock))
                        col.Item().AlignCenter().Text(doc.CompanyAddressBlock);
                    if (!string.IsNullOrWhiteSpace(doc.CompanyPhone))
                        col.Item().AlignCenter().Text(doc.CompanyPhone);
                    if (!string.IsNullOrWhiteSpace(doc.CompanyEmail))
                        col.Item().AlignCenter().Text(doc.CompanyEmail);

                    col.Item().PaddingVertical(4).LineHorizontal(0.5f);

                    // Order info
                    if (!string.IsNullOrWhiteSpace(doc.OrderNumber))
                        col.Item().Text($"Order: {doc.OrderNumber}").Bold();
                    col.Item().Text($"Date: {doc.OrderDate:yyyy-MM-dd}");
                    if (!string.IsNullOrWhiteSpace(doc.Carrier) || !string.IsNullOrWhiteSpace(doc.TrackingNumber))
                        col.Item().Text($"Ship: {doc.Carrier} {doc.TrackingNumber}".Trim());

                    // Customer
                    col.Item().PaddingTop(4).Text("Ship to:").Bold();
                    col.Item().Text(doc.CustomerName);
                    if (!string.IsNullOrWhiteSpace(doc.CustomerAddressBlock))
                        col.Item().Text(doc.CustomerAddressBlock);

                    col.Item().PaddingVertical(4).LineHorizontal(0.5f);

                    // Line items
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);      // name/set/cond
                            columns.ConstantColumn(24);     // qty
                            if (doc.ShowPrices)
                                columns.ConstantColumn(48); // line total
                        });

                        foreach (var line in doc.Lines)
                        {
                            var label = line.Name
                                        + (string.IsNullOrWhiteSpace(line.Set) ? "" : $" ({line.Set})")
                                        + (string.IsNullOrWhiteSpace(line.Condition) ? "" : $" {line.Condition}")
                                        + (line.IsFoil ? " *foil" : "");
                            table.Cell().PaddingVertical(1).Text(label);
                            table.Cell().PaddingVertical(1).AlignRight().Text($"x{line.Quantity}");
                            if (doc.ShowPrices)
                                table.Cell().PaddingVertical(1).AlignRight().Text($"${line.LineTotal:N2}");
                        }
                    });

                    // Totals
                    if (doc.ShowPrices)
                    {
                        col.Item().PaddingVertical(4).LineHorizontal(0.5f);
                        col.Item().AlignRight().Text($"Items: ${doc.ItemsTotal:N2}");
                        col.Item().AlignRight().Text($"Shipping: ${doc.Shipping:N2}");
                        col.Item().AlignRight().Text($"Total: ${doc.GrandTotal:N2}").Bold();
                    }

                    // Footer
                    if (!string.IsNullOrWhiteSpace(doc.FooterText))
                        col.Item().PaddingTop(8).AlignCenter().Text(doc.FooterText);
                });
            });
        }).GeneratePdf(filePath);
    }
}
