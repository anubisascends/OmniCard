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
        var path = Path.Combine(_tempDir, "manabox.csv");
        _service.ExportManabox(path, CreateTestCards());

        var lines = File.ReadAllLines(path);
        Assert.Contains("Card Name", lines[0]);
        Assert.Contains("Finish", lines[0]);
        Assert.Contains("Scryfall ID", lines[0]);
        Assert.Contains("Price (USD)", lines[0]);

        // Finish mapping
        Assert.Contains("nonfoil", lines[1]);
        Assert.Contains("foil", lines[2]);

        // Scryfall ID
        Assert.Contains("abc-123", lines[1]);
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
}
