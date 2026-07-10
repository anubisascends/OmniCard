using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using OmniCard.Models;
using OmniCard.Interfaces;
using OmniCard.Collection;

namespace OmniCard.Tests.Services;

public class CsvExportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CsvExportImportService _service;

    public CsvExportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _service = new CsvExportImportService(
            null!, // IDbContextFactory — not needed for export
            null!, // IScryfallService — not needed for export
            null!, // IStorageContainerService — not needed for export
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CsvExportImportService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private List<CollectionCard> CreateTestCards()
    {
        var container = new StorageContainer { Id = 1, Name = "Red Binder", ContainerType = ContainerType.Binder };
        return
        [
            new CollectionCard
            {
                Game = CardGame.Mtg,
                GameCardId = "abc-123",
                Name = "Lightning Bolt",
                SetName = "Alpha",
                SetCode = "LEA",
                Number = "161",
                Rarity = "common",
                Condition = "NM",
                IsFoil = false,
                PurchasePrice = 5.99m,
                DateAdded = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                ContainerId = 1,
                Container = container,
                Page = 3,
                Slot = 7,
            },
            new CollectionCard
            {
                Game = CardGame.Mtg,
                GameCardId = "def-456",
                Name = "Ach! Hans, Run!",
                SetName = "Unhinged",
                SetCode = "UNH",
                Number = "116",
                Rarity = "rare",
                Condition = "LP",
                IsFoil = true,
                PurchasePrice = null,
                DateAdded = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            },
        ];
    }

    [Fact]
    public void ExportAppNative_WritesAllColumnsWithLocation()
    {
        var path = Path.Combine(_tempDir, "native.csv");
        _service.ExportAppNative(path, CreateTestCards());

        var lines = File.ReadAllLines(path);
        Assert.True(lines.Length >= 3); // header + 2 rows

        // Header should contain key columns
        Assert.Contains("GameCardId", lines[0]);
        Assert.Contains("ContainerName", lines[0]);
        Assert.Contains("Page", lines[0]);

        // First data row should have location data
        Assert.Contains("Lightning Bolt", lines[1]);
        Assert.Contains("Red Binder", lines[1]);
        Assert.Contains("Binder", lines[1]);
    }

    [Fact]
    public void ExportAppNative_HandlesCommasInCardNames()
    {
        var path = Path.Combine(_tempDir, "commas.csv");
        _service.ExportAppNative(path, CreateTestCards());

        // CsvHelper should quote the card name with comma
        var content = File.ReadAllText(path);
        Assert.Contains("\"Ach! Hans, Run!\"", content);
    }

    [Fact]
    public void ExportTcgPlayer_MapsConditionAndPrinting()
    {
        var path = Path.Combine(_tempDir, "tcgplayer.csv");
        _service.ExportTcgPlayer(path, CreateTestCards());

        var lines = File.ReadAllLines(path);
        Assert.Contains("Quantity", lines[0]);
        Assert.Contains("Printing", lines[0]);

        // NM → "Near Mint", not foil → "Normal"
        Assert.Contains("Near Mint", lines[1]);
        Assert.Contains("Normal", lines[1]);

        // LP → "Lightly Played", foil → "Foil"
        Assert.Contains("Lightly Played", lines[2]);
        Assert.Contains("Foil", lines[2]);
    }

    [Fact]
    public void ExportMoxfield_MapsEditionAndFoil()
    {
        var path = Path.Combine(_tempDir, "moxfield.csv");
        _service.ExportMoxfield(path, CreateTestCards());

        var lines = File.ReadAllLines(path);
        Assert.Contains("Edition", lines[0]);
        Assert.Contains("Count", lines[0]);

        // SetCode as Edition
        Assert.Contains("LEA", lines[1]);

        // Foil card should have "foil"
        Assert.Contains("foil", lines[2]);
    }

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

    [Fact]
    public void ExportAppNative_EmptyCollection_WritesHeaderOnly()
    {
        var path = Path.Combine(_tempDir, "empty.csv");
        _service.ExportAppNative(path, []);

        var lines = File.ReadAllLines(path);
        Assert.Single(lines); // header only
        Assert.Contains("GameCardId", lines[0]);
    }

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
}
