using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using OmniCard.Models;

namespace OmniCard.Services;

/// <summary>Exports an AuditReport to a PDF file using QuestPDF.</summary>
public interface IAuditPdfExporter
{
    void Export(AuditReport report, string filePath);
}

public sealed class AuditPdfExporter : IAuditPdfExporter
{
    public void Export(AuditReport report, string filePath)
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
                    col.Item().Text($"Audit Report — {report.LocationName}")
                        .FontSize(18).Bold();
                    col.Item().Text($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm}")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(col =>
                {
                    // Summary row
                    col.Item().PaddingBottom(12).Row(row =>
                    {
                        row.RelativeItem().Text(t =>
                        {
                            t.Span("Expected: ").Bold();
                            t.Span($"{report.ExpectedCount}");
                        });
                        row.RelativeItem().Text(t =>
                        {
                            t.Span("Scanned: ").Bold();
                            t.Span($"{report.ActualCount}");
                        });
                        row.RelativeItem().Text(t =>
                        {
                            t.Span("Matched: ").Bold();
                            t.Span($"{report.Matched.Count}");
                        });
                        row.RelativeItem().Text(t =>
                        {
                            t.Span("Missing: ").Bold();
                            t.Span($"{report.Missing.Count}").FontColor(Colors.Red.Medium);
                        });
                        row.RelativeItem().Text(t =>
                        {
                            t.Span("Extra: ").Bold();
                            t.Span($"{report.Extra.Count}").FontColor(Colors.Orange.Medium);
                        });
                    });

                    // Missing cards table
                    if (report.Missing.Count > 0)
                    {
                        col.Item().PaddingTop(8).Text("Missing from Scan").FontSize(13).Bold();
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3); // Name
                                columns.RelativeColumn(1); // Set
                                columns.RelativeColumn(1); // Number
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Name").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Set").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("#").Bold();
                            });

                            foreach (var item in report.Missing)
                            {
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.Name ?? "Unknown");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.SetCode ?? "");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.CollectorNumber ?? "");
                            }
                        });
                    }

                    // Extra cards table
                    if (report.Extra.Count > 0)
                    {
                        col.Item().PaddingTop(16).Text("Not in Location (Extra)").FontSize(13).Bold();
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Name").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Set").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("#").Bold();
                            });

                            foreach (var item in report.Extra)
                            {
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.Name ?? "Unidentified");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.SetCode ?? "");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.CollectorNumber ?? "");
                            }
                        });
                    }

                    // Matched cards table
                    if (report.Matched.Count > 0)
                    {
                        col.Item().PaddingTop(16).Text("Matched").FontSize(13).Bold();
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Name").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Set").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("#").Bold();
                            });

                            foreach (var item in report.Matched)
                            {
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.Name ?? "");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.SetCode ?? "");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.CollectorNumber ?? "");
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
