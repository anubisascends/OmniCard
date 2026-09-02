using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Audit;

/// <summary>Renders a set's want-list (unowned cards + standard/foil prices + a blank tick-box)
/// to PDF for hunting cards away from a computer. Mirrors <see cref="PriceSheetPdfExporter"/>.</summary>
public sealed class SetChecklistPdfExporter : ISetChecklistPdfExporter
{
    private static readonly Color HeaderFill = Colors.Grey.Lighten2;
    private static readonly Color RowStripe = Colors.Grey.Lighten4;
    private static readonly Color GridLine = Colors.Grey.Lighten2;

    private static void HeaderCell(TableCellDescriptor header, string text, bool right = false)
    {
        var cell = header.Cell().Background(HeaderFill).Padding(4);
        (right ? cell.AlignRight() : cell).Text(text).Bold();
    }

    private static IContainer BodyCell(TableDescriptor table, Color fill) =>
        table.Cell().Background(fill).BorderBottom(1).BorderColor(GridLine).Padding(4);

    /// <summary>An empty, bordered box the user can tick by hand when they find the card.</summary>
    private static void CheckboxCell(TableDescriptor table, Color fill) =>
        table.Cell().Background(fill).BorderBottom(1).BorderColor(GridLine)
            .Padding(4).AlignCenter().AlignMiddle()
            .Width(12).Height(12).Border(1).BorderColor(Colors.Grey.Darken1);

    private static string Money(decimal? value) => value.HasValue ? $"${value.Value:N2}" : "—";

    public void Export(SetChecklistReport report, string filePath)
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
                    col.Item().Text($"{report.SetName} — Want List").FontSize(18).Bold();
                    col.Item().Text(
                        $"{report.Game} · {report.SetCode} · {report.OwnedCount} of {report.TotalCount} owned " +
                        $"({report.CompletionPercent:F1}%) · {report.Rows.Count} missing")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(24);   // Found (tick-box)
                        columns.RelativeColumn(1);    // Collector #
                        columns.RelativeColumn(3.4f); // Name
                        columns.RelativeColumn(1.4f); // Rarity
                        columns.RelativeColumn(1);    // Standard
                        columns.RelativeColumn(1);    // Foil
                    });

                    table.Header(header =>
                    {
                        HeaderCell(header, "");
                        HeaderCell(header, "No.");
                        HeaderCell(header, "Name");
                        HeaderCell(header, "Rarity");
                        HeaderCell(header, "Standard", right: true);
                        HeaderCell(header, "Foil", right: true);
                    });

                    var row = 0;
                    foreach (var line in report.Rows)
                    {
                        Color fill = row++ % 2 == 0 ? Colors.White : RowStripe;

                        CheckboxCell(table, fill);
                        BodyCell(table, fill).Text(line.CollectorNumber);
                        BodyCell(table, fill).Text(line.Name);
                        BodyCell(table, fill).Text(line.Rarity);
                        BodyCell(table, fill).AlignRight().Text(Money(line.NormalPrice));
                        BodyCell(table, fill).AlignRight().Text(Money(line.FoilPrice));
                    }

                    if (report.Rows.Count == 0)
                        table.Cell().ColumnSpan(6).Padding(12).AlignCenter()
                            .Text("Set complete — no cards missing.").Italic().FontColor(Colors.Grey.Medium);
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
