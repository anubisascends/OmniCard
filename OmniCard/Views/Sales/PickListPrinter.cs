using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

public static class PickListPrinter
{
    public static void Print(IReadOnlyList<PickListEntry> entries)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        var doc = new FlowDocument
        {
            PagePadding = new Thickness(40),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily("Segoe UI"),
        };

        doc.Blocks.Add(new Paragraph(
            new Run($"Pick List ({entries.Count} {(entries.Count == 1 ? "card" : "cards")})"))
        {
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12),
        });

        foreach (var entry in entries)
        {
            // Line 1: [checkbox]  Card Name (SET)
            var titleLine = new Paragraph { Margin = new Thickness(0, 0, 0, 0) };
            titleLine.Inlines.Add(new InlineUIContainer(
                new Border
                {
                    Width = 13,
                    Height = 13,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                })
            {
                BaselineAlignment = BaselineAlignment.Center,
            });
            var title = string.IsNullOrWhiteSpace(entry.SetCode)
                ? entry.Name
                : $"{entry.Name} ({entry.SetCode})";
            titleLine.Inlines.Add(new Run("  " + title) { FontSize = 13, FontWeight = FontWeights.SemiBold });
            doc.Blocks.Add(titleLine);

            // Line 2 (indented): Location Name      Section/Page/Slot (omit what's absent)
            var position = FormatPosition(entry);
            var locationText = string.IsNullOrWhiteSpace(position)
                ? entry.LocationName
                : $"{entry.LocationName}      {position}";
            doc.Blocks.Add(new Paragraph(new Run(locationText) { FontSize = 11 })
            {
                Margin = new Thickness(26, 1, 0, 10),
                Foreground = Brushes.DimGray,
            });
        }

        dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Pick List");
    }

    /// <summary>Compact "where in the location" string, skipping any absent parts.</summary>
    private static string FormatPosition(PickListEntry entry)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.Section)) parts.Add(entry.Section!);
        if (entry.Page is int page) parts.Add($"Pg {page}");
        if (entry.Slot is int slot) parts.Add($"Slot {slot}");
        return string.Join("   ", parts);
    }
}
