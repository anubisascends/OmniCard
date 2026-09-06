using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Audit;

/// <summary>Renders the pick list to PDF: one tickable row per card to pull, with where to find it
/// (location + section/page/slot). The web equivalent of the desktop's <c>PickListPrinter</c>
/// (which prints a WPF FlowDocument directly). Mirrors <see cref="SetChecklistPdfExporter"/>.</summary>
public sealed class PickListPdfExporter : IPickListPdfExporter
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

    private static void CheckboxCell(TableDescriptor table, Color fill) =>
        table.Cell().Background(fill).BorderBottom(1).BorderColor(GridLine)
            .Padding(4).AlignCenter().AlignMiddle()
            .Width(12).Height(12).Border(1).BorderColor(Colors.Grey.Darken1);

    /// <summary>Compact "where in the location" string, skipping any absent parts.</summary>
    private static string FormatPosition(PickListEntry e)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(e.Section)) parts.Add(e.Section!);
        if (e.Page is int page) parts.Add($"Pg {page}");
        if (e.Slot is int slot) parts.Add($"Slot {slot}");
        return string.Join("   ", parts);
    }

    public void Export(IReadOnlyList<PickListEntry> entries, string filePath)
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
                    col.Item().Text("Pick List").FontSize(18).Bold();
                    col.Item().Text($"{entries.Count} {(entries.Count == 1 ? "card" : "cards")} to pull")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(24);   // Pulled (tick-box)
                        columns.RelativeColumn(0.7f); // Qty
                        columns.RelativeColumn(3.4f); // Card (name + set)
                        columns.RelativeColumn(1.1f); // Condition
                        columns.RelativeColumn(2.2f); // Location
                        columns.RelativeColumn(1.6f); // Where (section/page/slot)
                        columns.RelativeColumn(1);    // Price
                    });

                    table.Header(header =>
                    {
                        HeaderCell(header, "");
                        HeaderCell(header, "Qty");
                        HeaderCell(header, "Card");
                        HeaderCell(header, "Cond");
                        HeaderCell(header, "Location");
                        HeaderCell(header, "Where");
                        HeaderCell(header, "Price", right: true);
                    });

                    var row = 0;
                    foreach (var e in entries)
                    {
                        Color fill = row++ % 2 == 0 ? Colors.White : RowStripe;
                        var name = string.IsNullOrWhiteSpace(e.SetCode) ? e.Name : $"{e.Name} ({e.SetCode})";
                        if (e.IsFoil) name += "  ✦";
                        var cond = string.IsNullOrWhiteSpace(e.Condition) ? "—" : e.Condition!;

                        CheckboxCell(table, fill);
                        BodyCell(table, fill).Text(e.Quantity.ToString());
                        BodyCell(table, fill).Text(name);
                        BodyCell(table, fill).Text(cond);
                        BodyCell(table, fill).Text(e.LocationName);
                        BodyCell(table, fill).Text(FormatPosition(e));
                        BodyCell(table, fill).AlignRight().Text($"${e.ListedPrice:N2}");
                    }

                    if (entries.Count == 0)
                        table.Cell().ColumnSpan(7).Padding(12).AlignCenter()
                            .Text("Nothing to pull — no active listings.").Italic().FontColor(Colors.Grey.Medium);
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
