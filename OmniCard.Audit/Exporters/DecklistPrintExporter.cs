using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using OmniCard.Shared.Audit;
using OmniCard.Shared.Lists;

namespace OmniCard.Audit.Exporters;

/// <summary>Renders a decklist check's pull list (owned copies to pull, grouped by location and walked
/// in section/page/slot order) and missing list (cards to acquire, with prices) to PDF — each a
/// tick-box checklist. Table styling mirrors <see cref="PickListPdfExporter"/>.</summary>
public sealed class DecklistPrintExporter : IDecklistPrintExporter
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

    private static string Plural(int n, string one, string other) => n == 1 ? one : other;

    /// <summary>Compact "where in the location" string, skipping any absent parts.</summary>
    private static string FormatPosition(DecklistPick p)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Section)) parts.Add(p.Section!);
        if (p.Page is int page) parts.Add($"Pg {page}");
        if (p.Slot is int slot) parts.Add($"Slot {slot}");
        return string.Join("   ", parts);
    }

    private static string FormatPrinting(string? setCode, string? collectorNumber)
    {
        if (string.IsNullOrWhiteSpace(setCode)) return "";
        return string.IsNullOrWhiteSpace(collectorNumber) ? setCode! : $"{setCode} #{collectorNumber}";
    }

    private static void Render(string title, string deckName, string summary, string? note,
        Action<IContainer> content, string filePath)
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
                    col.Item().Text(title).FontSize(18).Bold();
                    col.Item().Text(deckName).Bold();
                    col.Item().Text($"{summary}  ·  Generated {DateTime.Now:yyyy-MM-dd HH:mm}")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                    if (note is not null)
                        col.Item().PaddingTop(2).Text(note).FontSize(9).Italic().FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().PaddingTop(4).Element(content);

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

    public void ExportPullList(DecklistCheckResult result, string filePath, string title = "Pull List")
    {
        var picks = result.OwnedEntries
            .SelectMany(e => (e.Picks ?? []).Select(p => (Entry: e, Pick: p)))
            .ToList();
        var copies = picks.Sum(x => x.Pick.Quantity);
        var summary = $"{copies} {Plural(copies, "card", "cards")} to pull of {result.TotalCards}";
        var note = result.TotalMissing > 0
            ? $"{result.TotalMissing} {Plural(result.TotalMissing, "card isn't", "cards aren't")} in the collection."
            : null;

        var groups = picks
            .GroupBy(x => x.Pick.ContainerName)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Render(title, result.DeckName, summary, note, content => content.Column(col =>
        {
            if (groups.Count == 0)
            {
                col.Item().Padding(12).AlignCenter()
                    .Text("Nothing to pull — none of these cards are in the collection.").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            foreach (var group in groups)
            {
                var groupCopies = group.Sum(x => x.Pick.Quantity);
                col.Item().PaddingTop(8).Text($"{group.Key} ({groupCopies})").FontSize(12).Bold();
                col.Item().PaddingTop(2).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(24);   // Pulled (tick-box)
                        columns.RelativeColumn(0.6f); // Qty
                        columns.RelativeColumn(3.6f); // Card
                        columns.RelativeColumn(1.4f); // Printing (set + collector #)
                        columns.RelativeColumn(0.8f); // Condition
                        columns.RelativeColumn(1.8f); // Where (section/page/slot)
                    });

                    table.Header(header =>
                    {
                        HeaderCell(header, "");
                        HeaderCell(header, "Qty");
                        HeaderCell(header, "Card");
                        HeaderCell(header, "Printing");
                        HeaderCell(header, "Cond");
                        HeaderCell(header, "Where");
                    });

                    var row = 0;
                    var ordered = group
                        .OrderBy(x => x.Pick.Section ?? "", StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.Pick.Page ?? int.MaxValue)
                        .ThenBy(x => x.Pick.Slot ?? int.MaxValue)
                        .ThenBy(x => x.Entry.CardName, StringComparer.OrdinalIgnoreCase);
                    foreach (var (entry, pick) in ordered)
                    {
                        Color fill = row++ % 2 == 0 ? Colors.White : RowStripe;
                        var name = entry.CardName;
                        if (pick.IsFoil) name += "  ✦";
                        if (pick.IsListed) name += "  (listed for sale)";

                        CheckboxCell(table, fill);
                        BodyCell(table, fill).Text(pick.Quantity.ToString());
                        BodyCell(table, fill).Text(name);
                        BodyCell(table, fill).Text(FormatPrinting(pick.SetCode, pick.CollectorNumber));
                        BodyCell(table, fill).Text(string.IsNullOrWhiteSpace(pick.Condition) ? "—" : pick.Condition);
                        BodyCell(table, fill).Text(FormatPosition(pick));
                    }
                });
            }
        }), filePath);
    }

    public void ExportMissingList(DecklistCheckResult result, string filePath, string title = "Missing Cards")
    {
        var entries = result.MissingEntries
            .OrderBy(e => e.CardName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var summary = $"{result.TotalMissing} {Plural(result.TotalMissing, "card", "cards")} missing of {result.TotalCards}"
            + $"  ·  Est. ${result.EstimatedCost:N2} to complete";

        Render(title, result.DeckName, summary, null, content => content.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(24);   // Acquired (tick-box)
                columns.RelativeColumn(0.6f); // Qty
                columns.RelativeColumn(4);    // Card
                columns.RelativeColumn(1.4f); // Printing
                columns.RelativeColumn(1);    // Price
                columns.RelativeColumn(1);    // Subtotal
            });

            table.Header(header =>
            {
                HeaderCell(header, "");
                HeaderCell(header, "Qty");
                HeaderCell(header, "Card");
                HeaderCell(header, "Printing");
                HeaderCell(header, "Price", right: true);
                HeaderCell(header, "Subtotal", right: true);
            });

            var row = 0;
            foreach (var e in entries)
            {
                Color fill = row++ % 2 == 0 ? Colors.White : RowStripe;
                CheckboxCell(table, fill);
                BodyCell(table, fill).Text(e.QuantityNeeded.ToString());
                BodyCell(table, fill).Text(e.CardName);
                BodyCell(table, fill).Text(FormatPrinting(e.SetCode, e.CollectorNumber));
                BodyCell(table, fill).AlignRight().Text(e.MarketPrice is decimal p ? $"${p:N2}" : "—");
                BodyCell(table, fill).AlignRight()
                    .Text(e.MarketPrice is decimal s ? $"${s * e.QuantityNeeded:N2}" : "—");
            }

            if (entries.Count == 0)
            {
                table.Cell().ColumnSpan(6).Padding(12).AlignCenter()
                    .Text("Nothing missing — every card is in the collection.").Italic().FontColor(Colors.Grey.Medium);
            }
            else
            {
                table.Cell().ColumnSpan(5).Padding(4).AlignRight().Text("Estimated total").Bold();
                table.Cell().Padding(4).AlignRight().Text($"${result.EstimatedCost:N2}").Bold();
            }
        }), filePath);
    }

    public void ExportCardList(string listName, IReadOnlyList<ListPrintLine> lines, string filePath)
    {
        var ordered = lines.OrderBy(l => l.CardName, StringComparer.OrdinalIgnoreCase).ToList();
        var total = ordered.Sum(l => l.Quantity);
        var owned = ordered.Sum(l => Math.Min(l.OwnedQuantity, l.Quantity));
        var totalValue = ordered.Where(l => l.Price.HasValue).Sum(l => l.Price!.Value * l.Quantity);
        var toBuyValue = ordered.Where(l => l.Price.HasValue)
            .Sum(l => l.Price!.Value * Math.Max(0, l.Quantity - l.OwnedQuantity));
        var summary = $"{total} {Plural(total, "card", "cards")}  ·  {owned} in the collection  ·  {total - owned} to buy";

        Render("Card List", listName, summary, null, content => content.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(24);   // Tick-box
                columns.RelativeColumn(0.6f); // Qty
                columns.RelativeColumn(4);    // Card
                columns.RelativeColumn(1.4f); // Printing
                columns.RelativeColumn(0.8f); // Owned
                columns.RelativeColumn(1);    // Price
                columns.RelativeColumn(1);    // Subtotal
            });

            table.Header(header =>
            {
                HeaderCell(header, "");
                HeaderCell(header, "Qty");
                HeaderCell(header, "Card");
                HeaderCell(header, "Printing");
                HeaderCell(header, "Owned", right: true);
                HeaderCell(header, "Price", right: true);
                HeaderCell(header, "Subtotal", right: true);
            });

            var row = 0;
            foreach (var l in ordered)
            {
                Color fill = row++ % 2 == 0 ? Colors.White : RowStripe;
                CheckboxCell(table, fill);
                BodyCell(table, fill).Text(l.Quantity.ToString());
                BodyCell(table, fill).Text(l.IsFoil ? $"{l.CardName}  ✦" : l.CardName);
                BodyCell(table, fill).Text(FormatPrinting(l.SetCode, l.CollectorNumber));
                BodyCell(table, fill).AlignRight().Text(Math.Min(l.OwnedQuantity, l.Quantity).ToString());
                BodyCell(table, fill).AlignRight().Text(l.Price is decimal p ? $"${p:N2}" : "—");
                BodyCell(table, fill).AlignRight().Text(l.Price is decimal s ? $"${s * l.Quantity:N2}" : "—");
            }

            if (ordered.Count == 0)
            {
                table.Cell().ColumnSpan(7).Padding(12).AlignCenter()
                    .Text("This list is empty.").Italic().FontColor(Colors.Grey.Medium);
            }
            else
            {
                table.Cell().ColumnSpan(6).Padding(4).AlignRight().Text("Total value").Bold();
                table.Cell().Padding(4).AlignRight().Text($"${totalValue:N2}").Bold();
                table.Cell().ColumnSpan(6).Padding(4).AlignRight().Text("To buy");
                table.Cell().Padding(4).AlignRight().Text($"${toBuyValue:N2}");
            }
        }), filePath);
    }
}
