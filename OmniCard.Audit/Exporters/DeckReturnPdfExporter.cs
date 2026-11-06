using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using OmniCard.Shared.Audit;
using OmniCard.Shared.Storage;

namespace OmniCard.Audit.Exporters;

/// <summary>Renders a deck deconstruction's put-back report: every returned card grouped by the site and
/// location it goes to, walked in section/page/slot order, with a tick-box per line. Cards that couldn't go
/// back where they came from are marked and say why. Table styling mirrors <see cref="DecklistPrintExporter"/>.</summary>
public sealed class DeckReturnPdfExporter : IDeckReturnPdfExporter
{
    private static readonly Color HeaderFill = Colors.Grey.Lighten2;
    private static readonly Color RowStripe = Colors.Grey.Lighten4;
    private static readonly Color GridLine = Colors.Grey.Lighten2;
    private static readonly Color IssueFill = Colors.Orange.Lighten4;

    private static void HeaderCell(TableCellDescriptor header, string text)
    {
        header.Cell().Background(HeaderFill).Padding(4).Text(text).Bold();
    }

    private static IContainer BodyCell(TableDescriptor table, Color fill) =>
        table.Cell().Background(fill).BorderBottom(1).BorderColor(GridLine).Padding(4);

    private static void CheckboxCell(TableDescriptor table, Color fill) =>
        table.Cell().Background(fill).BorderBottom(1).BorderColor(GridLine)
            .Padding(4).AlignCenter().AlignMiddle()
            .Width(12).Height(12).Border(1).BorderColor(Colors.Grey.Darken1);

    private static string Plural(int n, string one, string other) => n == 1 ? one : other;

    /// <summary>"Section A   Pg 3   Slot 4", skipping absent parts.</summary>
    public static string FormatPosition(string? section, int? page, int? slot)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(section)) parts.Add(section!);
        if (page is int p) parts.Add($"Pg {p}");
        if (slot is int s) parts.Add($"Slot {s}");
        return string.Join("   ", parts);
    }

    private static string FormatPrinting(string? setCode, string? collectorNumber)
    {
        if (string.IsNullOrWhiteSpace(setCode)) return "";
        return string.IsNullOrWhiteSpace(collectorNumber) ? setCode!.ToUpperInvariant() : $"{setCode!.ToUpperInvariant()} #{collectorNumber}";
    }

    /// <summary>Why a line didn't go back where it came from, or null when it did.</summary>
    public static string? IssueText(DeckReturnLine l)
    {
        var where = l.OriginName is null ? null
            : FormatPosition(l.OriginSection, l.OriginPage, l.OriginSlot) is { Length: > 0 } pos ? $"{l.OriginName} ({pos.Replace("   ", ", ")})"
            : l.OriginName;
        return l.Issue switch
        {
            DeckReturnIssue.NoOrigin => "Where it came from isn't known.",
            DeckReturnIssue.LocationGone => "The location it came from was deleted.",
            DeckReturnIssue.NoAccess => $"Came from {where}, in a site you can't edit.",
            DeckReturnIssue.WrongGame => $"Came from {where}, which now holds another game.",
            DeckReturnIssue.SlotTaken => l.SlotTakenBy is { } taken
                ? $"Came from {where}; that pocket now holds {taken}."
                : $"Came from {where}; that pocket is now taken.",
            _ => null,
        };
    }

    public void Export(DeckReturnReport report, IReadOnlyList<DeckReturnLine> lines, string filePath)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.FontDiscoveryPaths.Clear();

        var copies = lines.Sum(l => l.Quantity);
        var flagged = lines.Where(l => l.Issue != DeckReturnIssue.None).Sum(l => l.Quantity);
        var summary = $"{copies} {Plural(copies, "card", "cards")} to put back";
        var note = flagged > 0
            ? $"{flagged} {Plural(flagged, "card", "cards")} couldn't go back where {Plural(flagged, "it", "they")} came from — marked (!) below."
            : null;

        var groups = lines
            .GroupBy(l => (l.SiteName, l.DestinationId, l.DestinationName))
            .OrderBy(g => g.Key.SiteName ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.DestinationName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var multipleSites = lines.Select(l => l.SiteName).Distinct().Count() > 1;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text(report.WholeDeck ? "Deck Deconstruction" : "Put-back List").FontSize(18).Bold();
                    col.Item().Text(report.DeckBoxName).Bold();
                    var by = report.CreatedBy is { Length: > 0 } user ? $" by {user}" : "";
                    col.Item().Text($"{summary}  ·  Returned {report.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}{by}")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                    if (note is not null)
                        col.Item().PaddingTop(2).Text(note).FontSize(9).Italic().FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().PaddingTop(4).Column(col =>
                {
                    if (groups.Count == 0)
                    {
                        col.Item().Padding(12).AlignCenter().Text("Nothing was returned.").Italic().FontColor(Colors.Grey.Medium);
                        return;
                    }

                    foreach (var group in groups)
                    {
                        var groupCopies = group.Sum(l => l.Quantity);
                        var heading = multipleSites && group.Key.SiteName is { } site
                            ? $"{site} / {group.Key.DestinationName}"
                            : group.Key.DestinationName;
                        col.Item().PaddingTop(8).Text($"{heading} ({groupCopies})").FontSize(12).Bold();
                        col.Item().PaddingTop(2).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(24);   // Put back (tick-box)
                                columns.RelativeColumn(0.5f); // Qty
                                columns.RelativeColumn(3.4f); // Card
                                columns.RelativeColumn(1.3f); // Printing
                                columns.RelativeColumn(0.7f); // Condition
                                columns.RelativeColumn(1.7f); // Where (section/page/slot)
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
                            foreach (var l in group)
                            {
                                var issue = IssueText(l);
                                Color fill = issue is not null ? IssueFill : row % 2 == 0 ? Colors.White : RowStripe;
                                row++;
                                var name = l.CardName;
                                if (l.IsFoil) name += "  ✦";
                                if (l.Language is { Length: > 0 } lang && lang != "en") name += $"  [{lang.ToUpperInvariant()}]";

                                CheckboxCell(table, fill);
                                BodyCell(table, fill).Text(l.Quantity.ToString());
                                BodyCell(table, fill).Column(c =>
                                {
                                    c.Item().Text(name);
                                    if (issue is not null)
                                        c.Item().Text($"(!) {issue}").FontSize(8).FontColor(Colors.Orange.Darken4);
                                    else if (l.RejoinedStack)
                                        c.Item().Text("Joins the stack already there").FontSize(8).FontColor(Colors.Grey.Darken1);
                                });
                                BodyCell(table, fill).Text(FormatPrinting(l.SetCode, l.CollectorNumber));
                                BodyCell(table, fill).Text(string.IsNullOrWhiteSpace(l.Condition) ? "—" : l.Condition);
                                BodyCell(table, fill).Text(FormatPosition(l.Section, l.Page, l.Slot) is { Length: > 0 } pos ? pos : "Loose");
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
