using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

public static class PickListPrinter
{
    public static void Print(IReadOnlyList<PickListEntry> entries)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        var doc = new FlowDocument { PagePadding = new Thickness(40), ColumnWidth = double.PositiveInfinity };
        doc.Blocks.Add(new Paragraph(new Run($"Pick List ({entries.Count} cards)")) { FontSize = 16, FontWeight = FontWeights.Bold });

        var table = new Table();
        for (int i = 0; i < 5; i++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        AddRow(group, "Location", "Slot", "Name", "Set", "Price", bold: true);
        foreach (var e in entries)
            AddRow(group, $"{e.LocationName} {e.Section}", $"{e.Page}/{e.Slot}", e.Name, e.SetName, e.ListedPrice.ToString("C"));
        table.RowGroups.Add(group);
        doc.Blocks.Add(table);

        dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Pick List");
    }

    private static void AddRow(TableRowGroup group, string a, string b, string c, string d, string e, bool bold = false)
    {
        var row = new TableRow();
        foreach (var text in new[] { a, b, c, d, e })
        {
            var p = new Paragraph(new Run(text));
            if (bold) p.FontWeight = FontWeights.Bold;
            row.Cells.Add(new TableCell(p));
        }
        group.Rows.Add(row);
    }
}
