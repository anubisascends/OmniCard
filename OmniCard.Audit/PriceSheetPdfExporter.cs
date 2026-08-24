using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Audit;

public sealed class PriceSheetPdfExporter : IPriceSheetPdfExporter
{
    private static readonly Color HeaderFill = Colors.Grey.Lighten2;
    private static readonly Color RowStripe = Colors.Grey.Lighten4;
    private static readonly Color GridLine = Colors.Grey.Lighten2;

    private static void HeaderCell(TableCellDescriptor header, string text, bool center = false)
    {
        var cell = header.Cell().Background(HeaderFill).Padding(4);
        (center ? cell.AlignCenter() : cell).Text(text).Bold();
    }

    private static IContainer BodyCell(TableDescriptor table, Color fill) =>
        table.Cell().Background(fill).BorderBottom(1).BorderColor(GridLine).Padding(4);

    /// <summary>An empty, bordered box the user can tick by hand.</summary>
    private static void CheckboxCell(TableDescriptor table, Color fill) =>
        table.Cell().Background(fill).BorderBottom(1).BorderColor(GridLine)
            .Padding(4).AlignCenter().AlignMiddle()
            .Width(12).Height(12).Border(1).BorderColor(Colors.Grey.Darken1);

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

                page.Content().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3.2f); // Name
                        columns.RelativeColumn(1.4f); // Game
                        columns.RelativeColumn(1.4f); // Card code
                        columns.RelativeColumn(1);    // Price
                        columns.ConstantColumn(38);   // Sold
                        columns.ConstantColumn(46);   // Traded
                        columns.ConstantColumn(40);   // Other
                    });

                    table.Header(header =>
                    {
                        HeaderCell(header, "Name");
                        HeaderCell(header, "Game");
                        HeaderCell(header, "Card Code");
                        header.Cell().Background(HeaderFill).Padding(4).AlignRight().Text("Price").Bold();
                        HeaderCell(header, "Sold", center: true);
                        HeaderCell(header, "Traded", center: true);
                        HeaderCell(header, "Other", center: true);
                    });

                    var row = 0;
                    foreach (var line in report.Lines)
                    {
                        Color fill = row++ % 2 == 0 ? Colors.White : RowStripe;

                        BodyCell(table, fill).Text(line.Name);
                        BodyCell(table, fill).Text(line.GameDisplayName);
                        BodyCell(table, fill).Text(line.CardCode);
                        BodyCell(table, fill).AlignRight().Text($"${line.Price:N2}");
                        CheckboxCell(table, fill);
                        CheckboxCell(table, fill);
                        CheckboxCell(table, fill);
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
