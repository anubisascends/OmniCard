using System.Globalization;
using System.Text;
using CsvHelper;

namespace OmniCard.Collection.Lists;

/// <summary>Which part of a list an export covers: every card, the copies still to buy, or the copies the
/// collection already covers (the same split as the Owned / To buy grids and the pick / buy lists).</summary>
public enum ListExportScope { All, ToBuy, Owned }

/// <summary>One export line: identical printings (same name, set, collector number and finish) are combined.</summary>
public sealed record ListExportLine(
    int Quantity, string CardName, string? SetCode, string? CollectorNumber, bool IsFoil, bool IsEtched = false);

/// <summary>Turns a planned list into plain-text decklist lines (<c>1x Aragorn, the Uniter (LTR) 192</c>, the
/// Moxfield/Archidekt import format, with <c>*F*</c> marking foils and <c>*E*</c> etched foils) or a CSV.</summary>
public static class ListExportFormatter
{
    public static IReadOnlyList<ListExportLine> Lines(IEnumerable<ListItemPlan> plan, ListExportScope scope) =>
        plan
            .Select(p => (p.Item, Quantity: scope switch
            {
                ListExportScope.Owned => Math.Min(p.OwnedQuantity, p.Item.Quantity),
                ListExportScope.ToBuy => p.MissingQuantity,
                _ => p.Item.Quantity,
            }))
            .Where(x => x.Quantity > 0)
            .GroupBy(x => (
                Name: x.Item.CardName.ToUpperInvariant(),
                Set: x.Item.SetCode?.ToUpperInvariant() ?? "",
                Number: x.Item.CollectorNumber ?? "",
                x.Item.IsFoil,
                IsEtched: x.Item.IsFoil && x.Item.FoilType?.Contains("etched", StringComparison.OrdinalIgnoreCase) == true))
            .Select(g => new ListExportLine(g.Sum(x => x.Quantity), g.First().Item.CardName,
                string.IsNullOrWhiteSpace(g.Key.Set) ? null : g.Key.Set,
                string.IsNullOrWhiteSpace(g.Key.Number) ? null : g.Key.Number,
                g.Key.IsFoil, g.Key.IsEtched))
            .OrderBy(l => l.CardName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.SetCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.CollectorNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.IsFoil)
            .ThenBy(l => l.IsEtched)
            .ToList();

    /// <summary><c>[Qty]x [Card Name] ([SET]) [Collector Number]</c>, plus <c> *F*</c> for foils (<c> *E*</c> for
    /// etched); a missing set or collector number is left out.</summary>
    public static string ToText(IEnumerable<ListExportLine> lines)
    {
        var sb = new StringBuilder();
        foreach (var l in lines)
        {
            sb.Append(l.Quantity.ToString(CultureInfo.InvariantCulture)).Append("x ").Append(l.CardName);
            if (l.SetCode is not null) sb.Append(" (").Append(l.SetCode).Append(')');
            if (l.CollectorNumber is not null) sb.Append(' ').Append(l.CollectorNumber);
            if (l.IsFoil) sb.Append(l.IsEtched ? " *E*" : " *F*");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static string ToCsv(IEnumerable<ListExportLine> lines)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        foreach (var header in new[] { "Qty", "Card Name", "Set", "Collector Number", "Foil" })
            csv.WriteField(header);
        csv.NextRecord();
        foreach (var l in lines)
        {
            csv.WriteField(l.Quantity);
            csv.WriteField(l.CardName);
            csv.WriteField(l.SetCode ?? "");
            csv.WriteField(l.CollectorNumber ?? "");
            csv.WriteField(l.IsEtched ? "Etched" : l.IsFoil ? "Foil" : "");
            csv.NextRecord();
        }
        csv.Flush();
        return writer.ToString();
    }
}
