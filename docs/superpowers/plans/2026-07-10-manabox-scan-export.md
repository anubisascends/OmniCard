# ManaBox Scan Queue Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add scan-queue export to ManaBox-native CSV and text formats, fix existing ManaBox export format, and clear the queue + temp images on export.

**Architecture:** Add condition mapping dicts and rewrite ExportManabox for the correct native format, add three new scan-aware export methods to ICsvExportImportService, wire three new commands in RootViewModel that export then clear.

**Tech Stack:** C# / .NET 10, CsvHelper, xUnit, CommunityToolkit.Mvvm

## Global Constraints

- Target framework: `net10.0-windows10.0.22621.0` with `UseWPF=true`
- Follow existing CsvExportImportService patterns (CsvHelper for CSV, StreamWriter for text)
- All export methods skip cards where match data is unavailable (`scan.Match == null` or missing fields)
- Condition mapping: NM→near_mint, LP→lightly_played, MP→moderately_played, HP→heavily_played, D→damaged
- ManaBox CSV columns use lowercase-style headers (`Set code`, not `Set Code`)
- No `ManaBox ID` column in any export
- Test project uses xUnit with `[Fact]`, temp directories, `NullLogger`, `null!` for unused constructor deps

---

### Task 1: Fix Existing ExportManabox Format

**Files:**
- Modify: `OmniCard.Collection/CsvExportImportService.cs`
- Modify: `OmniCard.Tests/Services/CsvExportTests.cs`
- Modify: `OmniCard.Tests/Services/CsvImportTests.cs`

**Interfaces:**
- Consumes: existing `ExportManabox(string, IEnumerable<CollectionCard>)` signature (unchanged)
- Produces: `ConditionToManabox` dict (used by Task 2), `ManaboxToCondition` dict (used by import), corrected 15-column CSV output

- [ ] **Step 1: Add condition mapping dictionaries**

Add these to `CsvExportImportService.cs` after the existing `TcgPlayerToCondition` dict (around line 29):

```csharp
private static readonly Dictionary<string, string> ConditionToManabox = new()
{
    ["NM"] = "near_mint",
    ["LP"] = "lightly_played",
    ["MP"] = "moderately_played",
    ["HP"] = "heavily_played",
    ["D"] = "damaged",
};

private static readonly Dictionary<string, string> ManaboxToCondition =
    ConditionToManabox.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);
```

- [ ] **Step 2: Rewrite ExportManabox method**

Replace the entire `ExportManabox` method body with the corrected 15-column format:

```csharp
public void ExportManabox(string filePath, IEnumerable<CollectionCard> cards)
{
    logger.LogInformation("Exporting collection to ManaBox CSV: {FilePath}", filePath);
    using var writer = new StreamWriter(filePath);
    using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

    // Header
    csv.WriteField("Name");
    csv.WriteField("Set code");
    csv.WriteField("Set name");
    csv.WriteField("Collector number");
    csv.WriteField("Foil");
    csv.WriteField("Rarity");
    csv.WriteField("Quantity");
    csv.WriteField("Scryfall ID");
    csv.WriteField("Purchase price");
    csv.WriteField("Misprint");
    csv.WriteField("Altered");
    csv.WriteField("Condition");
    csv.WriteField("Language");
    csv.WriteField("Purchase price currency");
    csv.WriteField("Added");
    csv.NextRecord();

    foreach (var card in cards)
    {
        csv.WriteField(card.Name);
        csv.WriteField(card.SetCode);
        csv.WriteField(card.SetName);
        csv.WriteField(card.Number);
        csv.WriteField(card.IsFoil ? "foil" : "normal");
        csv.WriteField(card.Rarity);
        csv.WriteField(1);
        csv.WriteField(card.GameCardId);
        csv.WriteField(card.PurchasePrice?.ToString(CultureInfo.InvariantCulture) ?? "");
        csv.WriteField(false);
        csv.WriteField(false);
        csv.WriteField(ConditionToManabox.GetValueOrDefault(card.Condition, "near_mint"));
        csv.WriteField("en");
        csv.WriteField("USD");
        csv.WriteField(card.DateAdded.ToString("o"));
        csv.NextRecord();
    }

    logger.LogInformation("ManaBox CSV export complete");
}
```

- [ ] **Step 3: Update DetectFormat**

Replace the Manabox detection line in `DetectFormat`:

```csharp
// Old:
if (headers.Contains("Finish") && headers.Contains("Card Name"))
    return CsvFormat.Manabox;

// New:
if (headers.Contains("Foil") && headers.Contains("Scryfall ID") && headers.Contains("Purchase price currency"))
    return CsvFormat.Manabox;
```

- [ ] **Step 4: Update ParseManaboxRow**

Replace the entire `ParseManaboxRow` method:

```csharp
private static CollectionCard ParseManaboxRow(CsvReader csv)
{
    var scryfallId = csv.GetField("Scryfall ID");
    var manaboxCondition = csv.GetField("Condition") ?? "near_mint";

    return new CollectionCard
    {
        Game = CardGame.Mtg,
        GameCardId = !string.IsNullOrEmpty(scryfallId) ? scryfallId : "",
        Name = csv.GetField("Name") ?? "",
        SetName = csv.GetField("Set name") ?? "",
        SetCode = csv.GetField("Set code") ?? "",
        Number = csv.GetField("Collector number") ?? "",
        Rarity = csv.GetField("Rarity") ?? "",
        Condition = ManaboxToCondition.GetValueOrDefault(manaboxCondition, "NM"),
        IsFoil = csv.GetField("Foil") == "foil",
        PurchasePrice = decimal.TryParse(csv.GetField("Purchase price"), CultureInfo.InvariantCulture, out var price) ? price : null,
        DateAdded = DateTime.UtcNow,
    };
}
```

- [ ] **Step 5: Update existing ExportManabox test**

In `CsvExportTests.cs`, replace `ExportManabox_WritesAllManaboxColumns`:

```csharp
[Fact]
public void ExportManabox_WritesAllManaboxColumns()
{
    var cards = CreateTestCards();
    var path = Path.Combine(_tempDir, "manabox.csv");
    _service.ExportManabox(path, cards);

    var lines = File.ReadAllLines(path);
    Assert.True(lines.Length >= 3);

    var header = lines[0];
    Assert.Contains("Name", header);
    Assert.Contains("Foil", header);
    Assert.Contains("Scryfall ID", header);
    Assert.Contains("Purchase price currency", header);
    Assert.DoesNotContain("Card Name", header);
    Assert.DoesNotContain("Finish", header);
    Assert.DoesNotContain("ManaBox ID", header);

    // Row 1: Lightning Bolt (NM, non-foil)
    Assert.Contains("normal", lines[1]);
    Assert.Contains("near_mint", lines[1]);
    Assert.Contains("abc-123", lines[1]);

    // Row 2: Ach! Hans, Run! (LP, foil)
    Assert.Contains("foil", lines[2]);
    Assert.Contains("lightly_played", lines[2]);
}
```

- [ ] **Step 6: Update existing import tests**

In `CsvImportTests.cs`, update `PreviewImport_DetectsManaboxFormat`:

```csharp
[Fact]
public void PreviewImport_DetectsManaboxFormat()
{
    var csv = "Name,Set code,Set name,Collector number,Foil,Rarity,Quantity,Scryfall ID,Purchase price,Misprint,Altered,Condition,Language,Purchase price currency,Added\n"
            + "Lightning Bolt,lea,Alpha,1,normal,common,1,abc-123,5.99,false,false,near_mint,en,USD,2026-01-01T00:00:00.0000000Z\n";
    var path = WriteCsv(csv);
    var svc = CreateService();

    var preview = svc.PreviewImport(path);

    Assert.Equal(CsvFormat.Manabox, preview.DetectedFormat);
    Assert.Single(preview.Cards);
    Assert.Equal("abc-123", preview.Cards[0].GameCardId);
    Assert.Equal("NM", preview.Cards[0].Condition);
}
```

Update `PreviewImport_ManaboxFinish_MapsFoilCorrectly`:

```csharp
[Fact]
public void PreviewImport_ManaboxFoil_MapsFoilCorrectly()
{
    var csv = "Name,Set code,Set name,Collector number,Foil,Rarity,Quantity,Scryfall ID,Purchase price,Misprint,Altered,Condition,Language,Purchase price currency,Added\n"
            + "Lightning Bolt,lea,Alpha,1,foil,common,1,abc-123,,false,false,near_mint,en,USD,2026-01-01T00:00:00.0000000Z\n";
    var path = WriteCsv(csv);
    var svc = CreateService();

    var preview = svc.PreviewImport(path);

    Assert.True(preview.Cards[0].IsFoil);
}
```

- [ ] **Step 7: Run all tests**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~CsvExport || FullyQualifiedName~CsvImport" -v normal
```

Expected: All export and import tests pass, including the updated ones.

- [ ] **Step 8: Commit**

```bash
git add OmniCard.Collection/CsvExportImportService.cs OmniCard.Tests/Services/CsvExportTests.cs OmniCard.Tests/Services/CsvImportTests.cs
git commit -m "fix: update ExportManabox to use native ManaBox format with correct column names and condition mapping"
```

---

### Task 2: Add Scan Export Methods + Tests

**Files:**
- Modify: `OmniCard.Shared/Interfaces/ICsvExportImportService.cs`
- Modify: `OmniCard.Collection/CsvExportImportService.cs`
- Modify: `OmniCard.Tests/Services/CsvExportTests.cs`

**Interfaces:**
- Consumes: `ConditionToManabox` dict from Task 1, `ScannedCard` model, `CardMatch` model, `StorageContainer` model
- Produces: `ExportManaboxScans(string, IEnumerable<ScannedCard>)`, `ExportManaboxScansCollection(string, IEnumerable<ScannedCard>)`, `ExportManaboxScansText(string, IEnumerable<ScannedCard>)` — used by Task 3 ViewModel commands

- [ ] **Step 1: Add method signatures to interface**

Add to `ICsvExportImportService.cs`:

```csharp
void ExportManaboxScans(string filePath, IEnumerable<ScannedCard> scans);
void ExportManaboxScansCollection(string filePath, IEnumerable<ScannedCard> scans);
void ExportManaboxScansText(string filePath, IEnumerable<ScannedCard> scans);
```

- [ ] **Step 2: Implement ExportManaboxScans**

Add to `CsvExportImportService.cs`:

```csharp
public void ExportManaboxScans(string filePath, IEnumerable<ScannedCard> scans)
{
    logger.LogInformation("Exporting scan queue to ManaBox CSV: {FilePath}", filePath);
    using var writer = new StreamWriter(filePath);
    using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

    csv.WriteField("Name");
    csv.WriteField("Set code");
    csv.WriteField("Set name");
    csv.WriteField("Collector number");
    csv.WriteField("Foil");
    csv.WriteField("Rarity");
    csv.WriteField("Quantity");
    csv.WriteField("Scryfall ID");
    csv.WriteField("Purchase price");
    csv.WriteField("Misprint");
    csv.WriteField("Altered");
    csv.WriteField("Condition");
    csv.WriteField("Language");
    csv.WriteField("Purchase price currency");
    csv.WriteField("Added");
    csv.NextRecord();

    var now = DateTime.UtcNow.ToString("o");
    foreach (var scan in scans)
    {
        if (scan.Match is null) continue;

        csv.WriteField(scan.Match.Name);
        csv.WriteField(scan.Match.SetCode);
        csv.WriteField(scan.Match.SetName);
        csv.WriteField(scan.Match.CollectorNumber);
        csv.WriteField(scan.IsFoil ? "foil" : "normal");
        csv.WriteField(scan.Match.Rarity);
        csv.WriteField(1);
        csv.WriteField(scan.Match.GameSpecificId);
        csv.WriteField(scan.PurchasePrice?.ToString(CultureInfo.InvariantCulture) ?? "");
        csv.WriteField(false);
        csv.WriteField(false);
        csv.WriteField(ConditionToManabox.GetValueOrDefault(scan.Condition, "near_mint"));
        csv.WriteField("en");
        csv.WriteField("USD");
        csv.WriteField(now);
        csv.NextRecord();
    }

    logger.LogInformation("ManaBox scan CSV export complete");
}
```

- [ ] **Step 3: Implement ExportManaboxScansCollection**

Add to `CsvExportImportService.cs`:

```csharp
public void ExportManaboxScansCollection(string filePath, IEnumerable<ScannedCard> scans)
{
    logger.LogInformation("Exporting scan queue to ManaBox collection CSV: {FilePath}", filePath);
    using var writer = new StreamWriter(filePath);
    using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

    csv.WriteField("Binder Name");
    csv.WriteField("Binder Type");
    csv.WriteField("Name");
    csv.WriteField("Set code");
    csv.WriteField("Set name");
    csv.WriteField("Collector number");
    csv.WriteField("Foil");
    csv.WriteField("Rarity");
    csv.WriteField("Quantity");
    csv.WriteField("Scryfall ID");
    csv.WriteField("Purchase price");
    csv.WriteField("Misprint");
    csv.WriteField("Altered");
    csv.WriteField("Condition");
    csv.WriteField("Language");
    csv.WriteField("Purchase price currency");
    csv.WriteField("Added");
    csv.NextRecord();

    var now = DateTime.UtcNow.ToString("o");
    foreach (var scan in scans)
    {
        if (scan.Match is null) continue;

        csv.WriteField(scan.OverrideContainer?.Name ?? "Scans");
        csv.WriteField(scan.OverrideContainer?.ContainerType.ToString().ToLowerInvariant() ?? "list");
        csv.WriteField(scan.Match.Name);
        csv.WriteField(scan.Match.SetCode);
        csv.WriteField(scan.Match.SetName);
        csv.WriteField(scan.Match.CollectorNumber);
        csv.WriteField(scan.IsFoil ? "foil" : "normal");
        csv.WriteField(scan.Match.Rarity);
        csv.WriteField(1);
        csv.WriteField(scan.Match.GameSpecificId);
        csv.WriteField(scan.PurchasePrice?.ToString(CultureInfo.InvariantCulture) ?? "");
        csv.WriteField(false);
        csv.WriteField(false);
        csv.WriteField(ConditionToManabox.GetValueOrDefault(scan.Condition, "near_mint"));
        csv.WriteField("en");
        csv.WriteField("USD");
        csv.WriteField(now);
        csv.NextRecord();
    }

    logger.LogInformation("ManaBox scan collection CSV export complete");
}
```

- [ ] **Step 4: Implement ExportManaboxScansText**

Add to `CsvExportImportService.cs`:

```csharp
public void ExportManaboxScansText(string filePath, IEnumerable<ScannedCard> scans)
{
    logger.LogInformation("Exporting scan queue to ManaBox text: {FilePath}", filePath);
    using var writer = new StreamWriter(filePath);

    foreach (var scan in scans)
    {
        if (scan.Match is null) continue;

        var line = $"1 {scan.Match.Name} ({scan.Match.SetCode}) {scan.Match.CollectorNumber}";
        if (scan.IsFoil)
            line += " *F*";
        writer.WriteLine(line);
    }

    logger.LogInformation("ManaBox scan text export complete");
}
```

- [ ] **Step 5: Add all 11 new tests**

Add these tests to `CsvExportTests.cs`. First add a helper method to create test ScannedCards:

```csharp
private static List<ScannedCard> CreateTestScannedCards()
{
    return
    [
        new ScannedCard
        {
            TempImagePath = "/tmp/scan1.png",
            Hash = 0x1234UL,
            Condition = "NM",
            IsFoil = false,
            PurchasePrice = 5.99m,
            Match = new CardMatch
            {
                Name = "Lightning Bolt",
                SetCode = "LEA",
                SetName = "Alpha",
                CollectorNumber = "161",
                Rarity = "common",
                GameSpecificId = "abc-123",
                Source = new object(),
            },
        },
        new ScannedCard
        {
            TempImagePath = "/tmp/scan2.png",
            Hash = 0x5678UL,
            Condition = "LP",
            IsFoil = true,
            Match = new CardMatch
            {
                Name = "Ach! Hans, Run!",
                SetCode = "UNH",
                SetName = "Unhinged",
                CollectorNumber = "116",
                Rarity = "rare",
                GameSpecificId = "def-456",
                Source = new object(),
            },
        },
    ];
}
```

Then add the tests:

```csharp
// --- ExportManaboxScans ---

[Fact]
public void ExportManaboxScans_WritesCorrectColumns()
{
    var scans = CreateTestScannedCards();
    var path = Path.Combine(_tempDir, "scans.csv");
    _service.ExportManaboxScans(path, scans);

    var lines = File.ReadAllLines(path);
    Assert.Equal(3, lines.Length); // header + 2 cards

    var header = lines[0];
    Assert.Contains("Name", header);
    Assert.Contains("Set code", header);
    Assert.Contains("Scryfall ID", header);
    Assert.DoesNotContain("Binder Name", header);

    Assert.Contains("Lightning Bolt", lines[1]);
    Assert.Contains("abc-123", lines[1]);
}

[Fact]
public void ExportManaboxScans_SkipsUnmatchedCards()
{
    var scans = new List<ScannedCard>
    {
        new() { TempImagePath = "/tmp/a.png", Hash = 1, Match = null },
        new()
        {
            TempImagePath = "/tmp/b.png", Hash = 2,
            Match = new CardMatch { Name = "Test", SetCode = "TST", SetName = "Test Set",
                CollectorNumber = "1", Rarity = "common", GameSpecificId = "id1", Source = new object() },
        },
    };
    var path = Path.Combine(_tempDir, "scans.csv");
    _service.ExportManaboxScans(path, scans);

    var lines = File.ReadAllLines(path);
    Assert.Equal(2, lines.Length); // header + 1 matched card
}

[Fact]
public void ExportManaboxScans_MapsFoilCorrectly()
{
    var scans = CreateTestScannedCards();
    var path = Path.Combine(_tempDir, "foil.csv");
    _service.ExportManaboxScans(path, scans);

    var lines = File.ReadAllLines(path);
    Assert.Contains(",normal,", lines[1]);
    Assert.Contains(",foil,", lines[2]);
}

[Fact]
public void ExportManaboxScans_MapsConditionCorrectly()
{
    var scans = CreateTestScannedCards();
    var path = Path.Combine(_tempDir, "condition.csv");
    _service.ExportManaboxScans(path, scans);

    var lines = File.ReadAllLines(path);
    Assert.Contains("near_mint", lines[1]);
    Assert.Contains("lightly_played", lines[2]);
}

[Fact]
public void ExportManaboxScans_EmptyQueue_WritesHeaderOnly()
{
    var path = Path.Combine(_tempDir, "empty.csv");
    _service.ExportManaboxScans(path, []);

    var lines = File.ReadAllLines(path);
    Assert.Single(lines);
    Assert.Contains("Name", lines[0]);
}

// --- ExportManaboxScansCollection ---

[Fact]
public void ExportManaboxScansCollection_IncludesBinderColumns()
{
    var scans = CreateTestScannedCards();
    var path = Path.Combine(_tempDir, "collection.csv");
    _service.ExportManaboxScansCollection(path, scans);

    var lines = File.ReadAllLines(path);
    Assert.StartsWith("Binder Name,Binder Type,", lines[0]);
}

[Fact]
public void ExportManaboxScansCollection_DefaultsToScansForUnassigned()
{
    var scans = CreateTestScannedCards(); // no OverrideContainer set
    var path = Path.Combine(_tempDir, "collection.csv");
    _service.ExportManaboxScansCollection(path, scans);

    var lines = File.ReadAllLines(path);
    Assert.StartsWith("Scans,list,", lines[1]);
}

[Fact]
public void ExportManaboxScansCollection_UsesOverrideContainer()
{
    var scans = CreateTestScannedCards();
    scans[0].OverrideContainer = new StorageContainer
    {
        Name = "My Binder",
        ContainerType = ContainerType.Binder,
    };
    var path = Path.Combine(_tempDir, "collection.csv");
    _service.ExportManaboxScansCollection(path, scans);

    var lines = File.ReadAllLines(path);
    Assert.StartsWith("My Binder,binder,", lines[1]);
    Assert.StartsWith("Scans,list,", lines[2]); // second card has no override
}

// --- ExportManaboxScansText ---

[Fact]
public void ExportManaboxScansText_WritesCorrectFormat()
{
    var scans = CreateTestScannedCards();
    var path = Path.Combine(_tempDir, "scans.txt");
    _service.ExportManaboxScansText(path, scans);

    var lines = File.ReadAllLines(path);
    Assert.Equal(2, lines.Length);
    Assert.Equal("1 Lightning Bolt (LEA) 161", lines[0]);
}

[Fact]
public void ExportManaboxScansText_AppendsFoilMarker()
{
    var scans = CreateTestScannedCards();
    var path = Path.Combine(_tempDir, "foil.txt");
    _service.ExportManaboxScansText(path, scans);

    var lines = File.ReadAllLines(path);
    Assert.DoesNotContain("*F*", lines[0]); // non-foil
    Assert.EndsWith("*F*", lines[1]);        // foil
}

[Fact]
public void ExportManaboxScansText_SkipsUnmatchedCards()
{
    var scans = new List<ScannedCard>
    {
        new() { TempImagePath = "/tmp/a.png", Hash = 1, Match = null },
        new()
        {
            TempImagePath = "/tmp/b.png", Hash = 2,
            Match = new CardMatch { Name = "Test", SetCode = "TST", SetName = "Test Set",
                CollectorNumber = "1", Rarity = "common", GameSpecificId = "id1", Source = new object() },
        },
    };
    var path = Path.Combine(_tempDir, "scans.txt");
    _service.ExportManaboxScansText(path, scans);

    var lines = File.ReadAllLines(path);
    Assert.Single(lines);
    Assert.StartsWith("1 Test (TST) 1", lines[0]);
}
```

- [ ] **Step 6: Run all tests**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~CsvExport" -v normal
```

Expected: All 17 tests pass (6 existing + 11 new).

- [ ] **Step 7: Commit**

```bash
git add OmniCard.Shared/Interfaces/ICsvExportImportService.cs OmniCard.Collection/CsvExportImportService.cs OmniCard.Tests/Services/CsvExportTests.cs
git commit -m "feat: add scan queue export to ManaBox CSV and text formats"
```

---

### Task 3: ViewModel Integration

**Files:**
- Modify: `OmniCard/Views/Root/RootViewModel.cs`

**Interfaces:**
- Consumes: `ICsvExportImportService.ExportManaboxScans`, `ExportManaboxScansCollection`, `ExportManaboxScansText` from Task 2; `CardService.ClearTempFiles()`, `CardService.ScannedCards`; `ExportToFile` helper (existing in RootViewModel)
- Produces: Three new RelayCommands callable from the UI

- [ ] **Step 1: Read existing export pattern**

Read `RootViewModel.cs` to find the existing `ExportAllManabox` and `ExportToFile` methods. Understand the exact pattern:
- `ExportToFile(string suggestedName, out string path)` — shows Save dialog, returns true if user picked a file
- Post-export: set `Message` property

Also find the `ClearScans` method pattern for queue clearing.

- [ ] **Step 2: Add the three export commands**

Add these methods near the existing `ExportAllManabox` method in `RootViewModel.cs`:

```csharp
[RelayCommand]
public void ExportScansManaboxCsv()
{
    if (IsAuditMode) return;
    var count = CardService.ScannedCards.Count;
    if (count == 0) return;

    if (!ExportToFile("scans-manabox.csv", out var path)) return;

    var scans = CardService.ScannedCards.ToList();
    csvService.ExportManaboxScans(path, scans);

    ResetScanFilterSort();
    SelectedScannedCards = [];
    SelectedScannedCard = null;
    NotifySelectionChanged();
    CardService.ClearTempFiles();
    CardService.ScannedCards.Clear();
    Message = $"Exported {count} cards to {Path.GetFileName(path)}. Scan queue cleared.";
}

[RelayCommand]
public void ExportScansManaboxCollectionCsv()
{
    if (IsAuditMode) return;
    var count = CardService.ScannedCards.Count;
    if (count == 0) return;

    if (!ExportToFile("scans-manabox-collection.csv", out var path)) return;

    var scans = CardService.ScannedCards.ToList();
    csvService.ExportManaboxScansCollection(path, scans);

    ResetScanFilterSort();
    SelectedScannedCards = [];
    SelectedScannedCard = null;
    NotifySelectionChanged();
    CardService.ClearTempFiles();
    CardService.ScannedCards.Clear();
    Message = $"Exported {count} cards to {Path.GetFileName(path)}. Scan queue cleared.";
}

[RelayCommand]
public void ExportScansManaboxText()
{
    if (IsAuditMode) return;
    var count = CardService.ScannedCards.Count;
    if (count == 0) return;

    if (!ExportToFile("scans-manabox.txt", out var path)) return;

    var scans = CardService.ScannedCards.ToList();
    csvService.ExportManaboxScansText(path, scans);

    ResetScanFilterSort();
    SelectedScannedCards = [];
    SelectedScannedCard = null;
    NotifySelectionChanged();
    CardService.ClearTempFiles();
    CardService.ScannedCards.Clear();
    Message = $"Exported {count} cards to {Path.GetFileName(path)}. Scan queue cleared.";
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build OmniCard/OmniCard.csproj
```

Expected: Build succeeds. The `[RelayCommand]` attribute generates `ExportScansManaboxCsvCommand`, `ExportScansManaboxCollectionCsvCommand`, and `ExportScansManaboxTextCommand` properties automatically.

- [ ] **Step 4: Run full test suite**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj -v normal
```

Expected: All tests pass (no regressions).

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/RootViewModel.cs
git commit -m "feat: wire scan queue export commands in RootViewModel with auto-clear"
```
