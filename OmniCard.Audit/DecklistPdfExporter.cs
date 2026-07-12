using System.IO;
using System.Net.Http;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Audit;

public sealed class DecklistPdfExporter(IHttpClientFactory httpClientFactory) : IDecklistPdfExporter
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
                    // Cards You Own — grouped by type
                    if (result.OwnedEntries.Count > 0)
                    {
                        col.Item().Text("Cards You Own").FontSize(13).Bold();

                        foreach (var typeGroup in GroupByType(result.OwnedEntries, e => e.TypeCategory))
                        {
                            col.Item().PaddingTop(8).Text($"{typeGroup.Key} ({typeGroup.Count()})")
                                .FontSize(11).Bold().FontColor(Colors.Grey.Darken1);

                            col.Item().PaddingTop(2).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2.5f);
                                    columns.RelativeColumn(0.7f);
                                    columns.RelativeColumn(0.5f);
                                    columns.RelativeColumn(4);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Card Name").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Set").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Qty").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Location(s)").Bold();
                                });

                                foreach (var entry in typeGroup.OrderBy(e => e.CardName, StringComparer.OrdinalIgnoreCase))
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
                    }

                    // Cards to Buy — grouped by type
                    if (result.MissingEntries.Count > 0)
                    {
                        col.Item().PaddingTop(16).Text("Cards to Buy").FontSize(13).Bold()
                            .FontColor(Colors.Red.Medium);

                        foreach (var typeGroup in GroupByType(result.MissingEntries, e => e.TypeCategory))
                        {
                            col.Item().PaddingTop(8).Text($"{typeGroup.Key} ({typeGroup.Count()})")
                                .FontSize(11).Bold().FontColor(Colors.Grey.Darken1);

                            col.Item().PaddingTop(2).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(3);
                                    columns.RelativeColumn(0.7f);
                                    columns.RelativeColumn(0.5f);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(1);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Card Name").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Set").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Qty").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Price").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Subtotal").Bold();
                                });

                                foreach (var entry in typeGroup.OrderBy(e => e.CardName, StringComparer.OrdinalIgnoreCase))
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
                            });
                        }

                        // Total row
                        col.Item().PaddingTop(8).AlignRight().Text($"Total: ${result.EstimatedCost:N2}")
                            .FontSize(12).Bold();
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

    private static IEnumerable<IGrouping<string, T>> GroupByType<T>(
        IEnumerable<T> entries, Func<T, string?> typeSelector)
    {
        var order = OmniCard.Collection.DecklistService.TypeCategoryOrder;
        return entries
            .GroupBy(e => typeSelector(e) ?? "Other")
            .OrderBy(g => Array.IndexOf(order, g.Key) is var i && i >= 0 ? i : order.Length);
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

    public void ExportDetailed(DecklistCheckResult result, string filePath)
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
                    col.Item().Text("Decklist Report — Detailed").FontSize(18).Bold();
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
                    // Owned cards
                    if (result.OwnedEntries.Count > 0)
                    {
                        col.Item().Text("Cards You Own").FontSize(13).Bold();
                        foreach (var typeGroup in GroupByType(result.OwnedEntries, e => e.TypeCategory))
                        {
                            col.Item().PaddingTop(8).Text($"{typeGroup.Key} ({typeGroup.Count()})")
                                .FontSize(11).Bold().FontColor(Colors.Grey.Darken1);

                            foreach (var entry in typeGroup.OrderBy(e => e.CardName, StringComparer.OrdinalIgnoreCase))
                            {
                                col.Item().PaddingTop(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                                    .Row(row =>
                                {
                                    // Card image
                                    row.ConstantItem(75).Padding(2).Element(c =>
                                    {
                                        var imageBytes = LoadImage(entry.LocalImagePath, entry.ImageUri);
                                        if (imageBytes is not null)
                                            c.Image(imageBytes).FitWidth();
                                        else
                                            c.Background(Colors.Grey.Lighten3).Height(100)
                                                .AlignCenter().AlignMiddle()
                                                .Text(entry.CardName).FontSize(7);
                                    });

                                    // Card details
                                    row.RelativeItem().PaddingLeft(8).Column(detail =>
                                    {
                                        detail.Item().Row(r =>
                                        {
                                            r.RelativeItem().Text(entry.CardName).Bold().FontSize(11);
                                            if (entry.ManaCost is not null)
                                                r.ConstantItem(100).AlignRight().Text(entry.ManaCost).FontSize(9);
                                        });
                                        detail.Item().Row(r =>
                                        {
                                            r.RelativeItem().Text(entry.TypeLine ?? "").FontSize(9)
                                                .FontColor(Colors.Grey.Darken1);
                                            if (entry.Power is not null && entry.Toughness is not null)
                                                r.ConstantItem(50).AlignRight()
                                                    .Text($"{entry.Power}/{entry.Toughness}").FontSize(9);
                                        });
                                        if (entry.OracleText is not null)
                                            detail.Item().PaddingTop(2).Text(entry.OracleText)
                                                .FontSize(8).Italic().FontColor(Colors.Grey.Darken2);
                                        detail.Item().PaddingTop(2).Text(t =>
                                        {
                                            t.Span($"Set: {entry.SetCode ?? "?"}").FontSize(8);
                                            if (entry.Rarity is not null)
                                            {
                                                t.Span($" | Rarity: {entry.Rarity}").FontSize(8);
                                            }
                                        });
                                        detail.Item().PaddingTop(1).Text($"Location: {FormatLocations(entry.Locations)}")
                                            .FontSize(8);
                                    });
                                });
                            }
                        }
                    }

                    // Missing cards
                    if (result.MissingEntries.Count > 0)
                    {
                        col.Item().PaddingTop(16).Text("Cards to Buy").FontSize(13).Bold()
                            .FontColor(Colors.Red.Medium);

                        foreach (var typeGroup in GroupByType(result.MissingEntries, e => e.TypeCategory))
                        {
                            col.Item().PaddingTop(8).Text($"{typeGroup.Key} ({typeGroup.Count()})")
                                .FontSize(11).Bold().FontColor(Colors.Grey.Darken1);

                            foreach (var entry in typeGroup.OrderBy(e => e.CardName, StringComparer.OrdinalIgnoreCase))
                            {
                                col.Item().PaddingTop(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                                    .Row(row =>
                                {
                                    row.ConstantItem(75).Padding(2).Element(c =>
                                    {
                                        var imageBytes = LoadImage(entry.LocalImagePath, entry.ImageUri);
                                        if (imageBytes is not null)
                                            c.Image(imageBytes).FitWidth();
                                        else
                                            c.Background(Colors.Grey.Lighten3).Height(100)
                                                .AlignCenter().AlignMiddle()
                                                .Text(entry.CardName).FontSize(7);
                                    });

                                    row.RelativeItem().PaddingLeft(8).Column(detail =>
                                    {
                                        detail.Item().Row(r =>
                                        {
                                            r.RelativeItem().Text(entry.CardName).Bold().FontSize(11);
                                            if (entry.ManaCost is not null)
                                                r.ConstantItem(100).AlignRight().Text(entry.ManaCost).FontSize(9);
                                        });
                                        detail.Item().Row(r =>
                                        {
                                            r.RelativeItem().Text(entry.TypeLine ?? "").FontSize(9)
                                                .FontColor(Colors.Grey.Darken1);
                                            if (entry.Power is not null && entry.Toughness is not null)
                                                r.ConstantItem(50).AlignRight()
                                                    .Text($"{entry.Power}/{entry.Toughness}").FontSize(9);
                                        });
                                        if (entry.OracleText is not null)
                                            detail.Item().PaddingTop(2).Text(entry.OracleText)
                                                .FontSize(8).Italic().FontColor(Colors.Grey.Darken2);
                                        detail.Item().PaddingTop(2).Text(t =>
                                        {
                                            t.Span($"Set: {entry.SetCode ?? "?"}").FontSize(8);
                                            if (entry.Rarity is not null)
                                                t.Span($" | Rarity: {entry.Rarity}").FontSize(8);
                                        });
                                        detail.Item().PaddingTop(1).Text(
                                            entry.MarketPrice.HasValue
                                                ? $"Market Price: ${entry.MarketPrice:N2}"
                                                : "Market Price: N/A")
                                            .FontSize(8).Bold();
                                    });
                                });
                            }
                        }

                        col.Item().PaddingTop(8).AlignRight().Text($"Total: ${result.EstimatedCost:N2}")
                            .FontSize(12).Bold();
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

    private byte[]? LoadImage(string? localPath, string? imageUri)
    {
        // Try local file first
        if (localPath is not null && File.Exists(localPath))
        {
            try { return File.ReadAllBytes(localPath); }
            catch { /* fall through */ }
        }

        // Try downloading from URI
        if (imageUri is not null && httpClientFactory is not null)
        {
            try
            {
                var client = httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");
                client.Timeout = TimeSpan.FromSeconds(10);
                var response = client.GetAsync(imageUri).GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                    return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            }
            catch { /* fall through */ }
        }

        return null;
    }
}
