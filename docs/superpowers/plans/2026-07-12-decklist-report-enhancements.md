# Decklist Report Enhancements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add type-category grouping to the existing summary report and add a new detailed report with card images and full card info.

**Architecture:** Extend the existing `OwnedDecklistEntry` and `MissingDecklistEntry` records with card detail fields (type, mana cost, oracle text, etc.). Add a `GetTypeCategory` helper to extract broad type from Scryfall's `TypeLine`. Update `DecklistService.CheckAgainstCollection` to populate the new fields. Update `DecklistPdfExporter.Export` to group by type. Add `ExportDetailed` for the image-based report. Update the dialog with two export buttons.

**Tech Stack:** C#/.NET 10, QuestPDF, System.Net.Http (image download), EF Core, WPF/CommunityToolkit.Mvvm.

## Global Constraints

- No new NuGet packages -- reuse existing dependencies only.
- QuestPDF uses `LicenseType.Community`.
- Type category priority order: Planeswalker, Creature, Instant, Sorcery, Artifact, Enchantment, Land, Other.
- Image loading: try `LocalImagePath` first, download from `ImageUri` if not on disk, placeholder if both fail.
- Card images sized ~1 inch wide in the detailed PDF.

---

### Task 1: Extend Data Models and Type Extraction

**Files:**
- Modify: `OmniCard.Shared/Models/DecklistCheckResult.cs`
- Modify: `OmniCard.Collection/DecklistService.cs`
- Test: `OmniCard.Tests/Services/DecklistMatchingTests.cs`

**Interfaces:**
- Consumes: Existing `OwnedDecklistEntry`, `MissingDecklistEntry` records; `CardMatch.Source` (which is a `Card` object with `TypeLine`, `ManaCost`, `OracleText`, `Power`, `Toughness`, `Rarity`, `ImageUris`, `LocalImagePath`)
- Produces:
  - Updated `OwnedDecklistEntry` with added fields: `string? TypeCategory`, `string? TypeLine`, `string? ManaCost`, `string? OracleText`, `string? Power`, `string? Toughness`, `string? Rarity`, `string? ImageUri`, `string? LocalImagePath`
  - Updated `MissingDecklistEntry` with same added fields
  - Static method `DecklistService.GetTypeCategory(string? typeLine)` returning one of: "Planeswalker", "Creature", "Instant", "Sorcery", "Artifact", "Enchantment", "Land", "Other"
  - Static field `DecklistService.TypeCategoryOrder` — `string[]` defining display order

- [ ] **Step 1: Write type extraction test**

Add to `OmniCard.Tests/Services/DecklistMatchingTests.cs`, a new test class at the bottom of the file (outside the existing class):

```csharp
public class TypeCategoryTests
{
    [Theory]
    [InlineData("Creature — Human Pirate", "Creature")]
    [InlineData("Artifact Creature — Construct", "Creature")]
    [InlineData("Legendary Planeswalker — Jace", "Planeswalker")]
    [InlineData("Instant", "Instant")]
    [InlineData("Sorcery", "Sorcery")]
    [InlineData("Artifact", "Artifact")]
    [InlineData("Legendary Enchantment", "Enchantment")]
    [InlineData("Basic Land — Mountain", "Land")]
    [InlineData("Enchantment Creature — God", "Creature")]
    [InlineData("Tribal Instant — Goblin", "Instant")]
    [InlineData(null, "Other")]
    [InlineData("", "Other")]
    public void GetTypeCategory_ReturnsCorrectCategory(string? typeLine, string expected)
    {
        Assert.Equal(expected, DecklistService.GetTypeCategory(typeLine));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "TypeCategory" --no-restore`
Expected: Build failure — `GetTypeCategory` does not exist.

- [ ] **Step 3: Add `GetTypeCategory` and `TypeCategoryOrder` to `DecklistService`**

Add to `OmniCard.Collection/DecklistService.cs`, after the `SectionHeaders` field:

```csharp
    public static readonly string[] TypeCategoryOrder =
        ["Planeswalker", "Creature", "Instant", "Sorcery", "Artifact", "Enchantment", "Land", "Other"];

    public static string GetTypeCategory(string? typeLine)
    {
        if (string.IsNullOrWhiteSpace(typeLine))
            return "Other";

        // Check in priority order — "Artifact Creature" should be "Creature"
        if (typeLine.Contains("Planeswalker", StringComparison.OrdinalIgnoreCase)) return "Planeswalker";
        if (typeLine.Contains("Creature", StringComparison.OrdinalIgnoreCase)) return "Creature";
        if (typeLine.Contains("Instant", StringComparison.OrdinalIgnoreCase)) return "Instant";
        if (typeLine.Contains("Sorcery", StringComparison.OrdinalIgnoreCase)) return "Sorcery";
        if (typeLine.Contains("Artifact", StringComparison.OrdinalIgnoreCase)) return "Artifact";
        if (typeLine.Contains("Enchantment", StringComparison.OrdinalIgnoreCase)) return "Enchantment";
        if (typeLine.Contains("Land", StringComparison.OrdinalIgnoreCase)) return "Land";
        return "Other";
    }
```

- [ ] **Step 4: Run type category tests**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "TypeCategory" --no-restore`
Expected: All 12 theory cases PASS.

- [ ] **Step 5: Update record types with new fields**

Replace the `OwnedDecklistEntry` and `MissingDecklistEntry` records in `OmniCard.Shared/Models/DecklistCheckResult.cs`:

```csharp
public record OwnedDecklistEntry(
    string CardName,
    string? SetCode,
    string? CollectorNumber,
    int QuantityNeeded,
    List<DecklistCardLocation> Locations,
    string? TypeCategory = null,
    string? TypeLine = null,
    string? ManaCost = null,
    string? OracleText = null,
    string? Power = null,
    string? Toughness = null,
    string? Rarity = null,
    string? ImageUri = null,
    string? LocalImagePath = null);

public record MissingDecklistEntry(
    string CardName,
    string? SetCode,
    string? CollectorNumber,
    int QuantityNeeded,
    decimal? MarketPrice,
    string? TypeCategory = null,
    string? TypeLine = null,
    string? ManaCost = null,
    string? OracleText = null,
    string? Power = null,
    string? Toughness = null,
    string? Rarity = null,
    string? ImageUri = null,
    string? LocalImagePath = null);
```

- [ ] **Step 6: Update `CheckAgainstCollection` to populate new fields**

In `OmniCard.Collection/DecklistService.cs`, update `CheckAgainstCollection`. The card detail lookup should happen once per entry (not just for missing cards). Move the `SearchCards` call before the owned/missing split and extract card details.

Replace the body of the `foreach (var entry in entries)` loop with:

```csharp
        foreach (var entry in entries)
        {
            // Find all owned copies by name (case-insensitive)
            var ownedCopies = allCards
                .Where(c => string.Equals(c.Name, entry.CardName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Sort: exact set matches first, then others
            if (entry.SetCode is not null)
            {
                ownedCopies = ownedCopies
                    .OrderByDescending(c => string.Equals(c.SetCode, entry.SetCode, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var locations = ownedCopies.Select(c => new DecklistCardLocation(
                ContainerName: c.Container?.Name ?? "Unknown",
                Page: c.Page,
                Slot: c.Slot,
                Section: c.Section,
                SetCode: c.SetCode,
                IsFoil: c.IsFoil,
                IsExactSetMatch: entry.SetCode is not null &&
                    string.Equals(c.SetCode, entry.SetCode, StringComparison.OrdinalIgnoreCase)
            )).ToList();

            var ownedCount = Math.Min(ownedCopies.Count, entry.Quantity);
            var missingCount = entry.Quantity - ownedCount;

            // Look up card details from Scryfall DB for type/image/detail info
            var gameService = cardService.GetGameService(CardGame.Mtg);
            var searchResults = gameService.SearchCards($"name:{entry.CardName}");
            CardMatch? cardInfo = null;
            if (searchResults.Count > 0)
            {
                cardInfo = entry.SetCode is not null
                    ? searchResults.FirstOrDefault(r =>
                        string.Equals(r.SetCode, entry.SetCode, StringComparison.OrdinalIgnoreCase))
                      ?? searchResults[0]
                    : searchResults[0];
            }

            // Extract detail fields from the Card source object
            string? typeLine = null, manaCost = null, oracleText = null;
            string? power = null, toughness = null, rarity = null;
            string? imageUri = null, localImagePath = null;
            if (cardInfo?.Source is Card card)
            {
                typeLine = card.TypeLine;
                manaCost = card.ManaCost;
                oracleText = card.OracleText;
                power = card.Power;
                toughness = card.Toughness;
                rarity = card.Rarity;
                imageUri = card.ImageUris?.Normal ?? card.ImageUris?.Small;
                localImagePath = card.LocalImagePath;
            }
            var typeCategory = GetTypeCategory(typeLine);

            if (ownedCount > 0)
            {
                ownedEntries.Add(new OwnedDecklistEntry(
                    entry.CardName, entry.SetCode, entry.CollectorNumber,
                    ownedCount, locations,
                    typeCategory, typeLine, manaCost, oracleText,
                    power, toughness, rarity, imageUri, localImagePath));
            }

            if (missingCount > 0)
            {
                decimal? price = cardInfo is not null
                    ? gameService.GetCurrentPrice(cardInfo.GameSpecificId, false)
                    : null;

                missingEntries.Add(new MissingDecklistEntry(
                    entry.CardName, entry.SetCode, entry.CollectorNumber,
                    missingCount, price,
                    typeCategory, typeLine, manaCost, oracleText,
                    power, toughness, rarity, imageUri, localImagePath));
            }
        }
```

- [ ] **Step 7: Run all decklist tests**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "Decklist" --no-restore`
Expected: All existing tests PASS (records use default params so existing constructors still work). New TypeCategory tests also PASS.

- [ ] **Step 8: Commit**

```bash
git add OmniCard.Shared/Models/DecklistCheckResult.cs OmniCard.Collection/DecklistService.cs OmniCard.Tests/Services/DecklistMatchingTests.cs
git commit -m "feat(decklist): add type category extraction and card detail fields to result models"
```

---

### Task 2: Update Summary Report with Type Grouping

**Files:**
- Modify: `OmniCard.Audit/DecklistPdfExporter.cs`
- Test: `OmniCard.Tests/Services/DecklistPdfExporterTests.cs`

**Interfaces:**
- Consumes: `OwnedDecklistEntry.TypeCategory`, `MissingDecklistEntry.TypeCategory`, `DecklistService.TypeCategoryOrder` (Task 1)
- Produces: Updated `DecklistPdfExporter.Export` method that groups cards by type category

- [ ] **Step 1: Update the PDF export test to include type categories**

Replace the `Export_CreatesValidPdfFile` test in `OmniCard.Tests/Services/DecklistPdfExporterTests.cs`:

```csharp
    [Fact]
    public void Export_CreatesValidPdfFile()
    {
        var result = new DecklistCheckResult
        {
            DeckName = "Test Deck",
            DeckSource = "Moxfield",
            OwnedEntries =
            [
                new OwnedDecklistEntry("Lightning Bolt", "M11", "149", 1,
                [
                    new DecklistCardLocation("Binder A", 3, 2, null, "M11", false, true)
                ], TypeCategory: "Instant"),
                new OwnedDecklistEntry("Ragavan, Nimble Pilferer", "MH2", "138", 1,
                [
                    new DecklistCardLocation("Bulk", null, null, null, "MH2", false, true)
                ], TypeCategory: "Creature")
            ],
            MissingEntries =
            [
                new MissingDecklistEntry("Counterspell", "MH2", "267", 2, 1.50m,
                    TypeCategory: "Instant"),
                new MissingDecklistEntry("Tarmogoyf", "MH2", "187", 1, 55.00m,
                    TypeCategory: "Creature")
            ],
        };

        var exporter = new DecklistPdfExporter();
        var path = Path.Combine(_tempDir, "test_report.pdf");
        exporter.Export(result, path);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 100);
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }
```

- [ ] **Step 2: Update `Export` method to group by type**

Replace the content section of the `Export` method in `OmniCard.Audit/DecklistPdfExporter.cs`. The full updated `Export` method:

```csharp
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
```

- [ ] **Step 3: Run PDF tests**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "DecklistPdfExporter" --no-restore`
Expected: Both tests PASS.

- [ ] **Step 4: Commit**

```bash
git add OmniCard.Audit/DecklistPdfExporter.cs OmniCard.Tests/Services/DecklistPdfExporterTests.cs
git commit -m "feat(decklist): group summary report by card type category"
```

---

### Task 3: Detailed Report with Images and Dialog Integration

**Files:**
- Modify: `OmniCard.Shared/Interfaces/IDecklistPdfExporter.cs`
- Modify: `OmniCard.Audit/DecklistPdfExporter.cs`
- Modify: `OmniCard/Views/DecklistCheck/DecklistCheckViewModel.cs`
- Modify: `OmniCard/Views/DecklistCheck/DecklistCheckView.xaml`
- Modify: `OmniCard/Views/DecklistCheck/DecklistCheckView.xaml.cs`
- Test: `OmniCard.Tests/Services/DecklistPdfExporterTests.cs`

**Interfaces:**
- Consumes: `DecklistCheckResult` with populated detail fields (Task 1), `GroupByType` helper (Task 2), `FormatLocations` (existing), `IHttpClientFactory`
- Produces: `IDecklistPdfExporter.ExportDetailed(DecklistCheckResult result, string filePath, IHttpClientFactory httpClientFactory)`, `DecklistCheckViewModel.GenerateDetailedReportCommand`, two export buttons in the dialog

- [ ] **Step 1: Add `ExportDetailed` to interface**

Update `OmniCard.Shared/Interfaces/IDecklistPdfExporter.cs`:

```csharp
using System.Net.Http;
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IDecklistPdfExporter
{
    void Export(DecklistCheckResult result, string filePath);
    void ExportDetailed(DecklistCheckResult result, string filePath, IHttpClientFactory httpClientFactory);
}
```

- [ ] **Step 2: Add detailed export test**

Add to `OmniCard.Tests/Services/DecklistPdfExporterTests.cs`:

```csharp
    [Fact]
    public void ExportDetailed_CreatesValidPdfFile()
    {
        var result = new DecklistCheckResult
        {
            DeckName = "Detail Test",
            DeckSource = "Archidekt",
            OwnedEntries =
            [
                new OwnedDecklistEntry("Lightning Bolt", "M11", "149", 1,
                [
                    new DecklistCardLocation("Binder A", 3, 2, null, "M11", false, true)
                ],
                TypeCategory: "Instant", TypeLine: "Instant",
                ManaCost: "{R}", OracleText: "Lightning Bolt deals 3 damage to any target.",
                Rarity: "common")
            ],
            MissingEntries =
            [
                new MissingDecklistEntry("Ragavan, Nimble Pilferer", "MH2", "138", 1, 55.00m,
                    TypeCategory: "Creature",
                    TypeLine: "Legendary Creature \u2014 Monkey Pirate",
                    ManaCost: "{R}",
                    OracleText: "Whenever Ragavan, Nimble Pilferer deals combat damage to a player, create a Treasure token and exile the top card of that player's library. Until end of turn, you may cast that card.",
                    Power: "2", Toughness: "1", Rarity: "mythic")
            ],
        };

        var exporter = new DecklistPdfExporter();
        var path = Path.Combine(_tempDir, "detail_report.pdf");
        exporter.ExportDetailed(result, path, null!);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
    }
```

- [ ] **Step 3: Implement `ExportDetailed`**

Add to `OmniCard.Audit/DecklistPdfExporter.cs`:

```csharp
    public void ExportDetailed(DecklistCheckResult result, string filePath, IHttpClientFactory httpClientFactory)
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
                                        var imageBytes = LoadImage(entry.LocalImagePath, entry.ImageUri, httpClientFactory);
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
                                        var imageBytes = LoadImage(entry.LocalImagePath, entry.ImageUri, httpClientFactory);
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

    private static byte[]? LoadImage(string? localPath, string? imageUri, IHttpClientFactory? httpClientFactory)
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
```

Add `using System.IO;` and `using System.Net.Http;` to the top of `DecklistPdfExporter.cs`.

- [ ] **Step 4: Add `GenerateDetailedReportCommand` to ViewModel**

Add to `OmniCard/Views/DecklistCheck/DecklistCheckViewModel.cs`:

```csharp
    public Action<DecklistCheckResult>? ExportDetailedPdf { get; set; }

    [RelayCommand]
    public void GenerateDetailedReport()
    {
        if (Result is null) return;
        ExportDetailedPdf?.Invoke(Result);
    }
```

- [ ] **Step 5: Update dialog XAML — replace single button with two**

In `OmniCard/Views/DecklistCheck/DecklistCheckView.xaml`, replace the button section:

```xml
        <!-- Status bar and buttons -->
        <DockPanel Grid.Row="4" Margin="0,12,0,0">
            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                <Button Content="Summary Report" Padding="16,6" Margin="0,0,8,0"
                        Command="{Binding ViewModel.GenerateReportCommand}"
                        IsEnabled="{Binding ViewModel.Result, Converter={conv:NullToBoolConverter}}"/>
                <Button Content="Detailed Report" Padding="16,6" Margin="0,0,8,0"
                        Command="{Binding ViewModel.GenerateDetailedReportCommand}"
                        IsEnabled="{Binding ViewModel.Result, Converter={conv:NullToBoolConverter}}"/>
                <Button Content="Close" Padding="16,6" Click="OnClose"/>
            </StackPanel>
            <TextBlock Text="{Binding ViewModel.StatusMessage}"
                       VerticalAlignment="Center" TextWrapping="Wrap"
                       Foreground="{DynamicResource MaterialDesign.Brush.Primary}"/>
        </DockPanel>
```

- [ ] **Step 6: Wire detailed export callback in code-behind**

Update `OmniCard/Views/DecklistCheck/DecklistCheckView.xaml.cs` constructor. Add `IHttpClientFactory` parameter and wire the second callback:

```csharp
using System.Net.Http;
using System.Windows;
using Microsoft.Win32;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.DecklistCheck;

public partial class DecklistCheckView : Window
{
    public DecklistCheckViewModel ViewModel { get; }

    public DecklistCheckView(DecklistCheckViewModel viewModel, IDecklistPdfExporter pdfExporter,
        IHttpClientFactory httpClientFactory)
    {
        ViewModel = viewModel;
        DataContext = this;

        viewModel.ExportPdf = result =>
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files|*.pdf",
                FileName = $"Decklist_{result.DeckName}_{DateTime.Now:yyyy-MM-dd}.pdf"
            };
            if (dlg.ShowDialog() == true)
            {
                pdfExporter.Export(result, dlg.FileName);
                MessageBox.Show("PDF exported successfully.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };

        viewModel.ExportDetailedPdf = result =>
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files|*.pdf",
                FileName = $"Decklist_{result.DeckName}_Detailed_{DateTime.Now:yyyy-MM-dd}.pdf"
            };
            if (dlg.ShowDialog() == true)
            {
                pdfExporter.ExportDetailed(result, dlg.FileName, httpClientFactory);
                MessageBox.Show("Detailed PDF exported successfully.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };

        InitializeComponent();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 7: Build and run all tests**

Run: `dotnet build OmniCard/OmniCard.csproj --no-restore`
Expected: 0 errors.

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "Decklist" --no-restore`
Expected: All tests PASS (including the new `ExportDetailed_CreatesValidPdfFile`).

- [ ] **Step 8: Commit**

```bash
git add OmniCard.Shared/Interfaces/IDecklistPdfExporter.cs OmniCard.Audit/DecklistPdfExporter.cs OmniCard/Views/DecklistCheck/ OmniCard.Tests/Services/DecklistPdfExporterTests.cs
git commit -m "feat(decklist): add detailed report with card images and two export buttons"
```
