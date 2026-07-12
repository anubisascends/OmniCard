using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Audit;

public sealed class DecklistPdfExporter : IDecklistPdfExporter
{
    public void Export(DecklistCheckResult result, string filePath)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("Decklist Report").FontSize(18).Bold();
                    col.Item().Text(t =>
                    {
                        t.Span($"{result.DeckName}").Bold();
                        t.Span($" — Imported from {result.DeckSource}").FontColor(Colors.Grey.Medium);
                    });
                    col.Item().Text($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingTop(4).Text(t =>
                    {
                        t.Span($"Owned: {result.TotalOwned}/{result.TotalCards}").Bold();
                        t.Span("  |  ");
                        t.Span($"Missing: {result.TotalMissing}").Bold()
                            .FontColor(result.TotalMissing > 0 ? Colors.Red.Medium : Colors.Green.Medium);
                        t.Span("  |  ");
                        t.Span($"Estimated cost: ${result.EstimatedCost:N2}").Bold();
                    });
                    col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(col =>
                {
                    // Cards You Own
                    if (result.OwnedEntries.Count > 0)
                    {
                        col.Item().Text("Cards You Own").FontSize(13).Bold();
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2.5f); // Name
                                columns.RelativeColumn(0.7f); // Set
                                columns.RelativeColumn(0.5f); // Qty
                                columns.RelativeColumn(4);    // Location(s)
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Card Name").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Set").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Qty").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Location(s)").Bold();
                            });

                            foreach (var entry in result.OwnedEntries)
                            {
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(entry.CardName);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(entry.SetCode ?? "");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text($"{entry.QuantityNeeded}");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(FormatLocations(entry.Locations));
                            }
                        });
                    }

                    // Cards to Buy
                    if (result.MissingEntries.Count > 0)
                    {
                        col.Item().PaddingTop(16).Text("Cards to Buy").FontSize(13).Bold()
                            .FontColor(Colors.Red.Medium);
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);   // Name
                                columns.RelativeColumn(0.7f); // Set
                                columns.RelativeColumn(0.5f); // Qty
                                columns.RelativeColumn(1);   // Market Price
                                columns.RelativeColumn(1);   // Subtotal
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Card Name").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Set").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Qty").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Price").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Subtotal").Bold();
                            });

                            foreach (var entry in result.MissingEntries)
                            {
                                var priceStr = entry.MarketPrice.HasValue ? $"${entry.MarketPrice:N2}" : "N/A";
                                var subtotalStr = entry.MarketPrice.HasValue
                                    ? $"${entry.MarketPrice.Value * entry.QuantityNeeded:N2}" : "N/A";

                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(entry.CardName);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(entry.SetCode ?? "");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text($"{entry.QuantityNeeded}");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(priceStr);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(subtotalStr);
                            }

                            // Total row
                            table.Cell().ColumnSpan(4).Padding(4).AlignRight().Text("Total:").Bold();
                            table.Cell().Padding(4).Text($"${result.EstimatedCost:N2}").Bold();
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

    private static string FormatLocations(List<DecklistCardLocation> locations)
    {
        return string.Join("; ", locations.Select(loc =>
        {
            var parts = new List<string> { loc.ContainerName };
            if (loc.Page.HasValue) parts.Add($"Page {loc.Page}");
            if (loc.Slot.HasValue) parts.Add($"Slot {loc.Slot}");
            if (loc.Section is not null) parts.Add(loc.Section);

            var locationStr = string.Join(", ", parts);
            var suffix = new List<string>();
            if (loc.SetCode is not null) suffix.Add(loc.SetCode);
            if (loc.IsFoil) suffix.Add("Foil");
            if (loc.IsExactSetMatch) suffix.Add("\u2605"); // star character

            return suffix.Count > 0 ? $"{locationStr} ({string.Join(", ", suffix)})" : locationStr;
        }));
    }
}
