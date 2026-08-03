using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Audit;

public sealed class PriceSheetPdfExporter : IPriceSheetPdfExporter
{
    public void Export(PriceSheetReport report, string filePath)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.FontDiscoveryPaths.Clear();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text($"Price Sheet — {report.LocationName}")
                        .FontSize(18).Bold();
                    col.Item().Text($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm}")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(col =>
                {
                    foreach (var section in report.Sections)
                    {
                        col.Item().PaddingTop(12).Text(section.GameDisplayName).FontSize(13).Bold();
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3); // Name
                                columns.RelativeColumn(1); // Set
                                columns.RelativeColumn(1); // Collector #
                                columns.RelativeColumn(1); // Price
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Name").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Set").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("#").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Price").Bold();
                            });

                            foreach (var line in section.Lines)
                            {
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(line.Name);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(line.SetCode ?? "");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(line.CollectorNumber ?? "");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .AlignRight().Text($"${line.Price:N2}");
                            }
                        });
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf(filePath);
    }
}
