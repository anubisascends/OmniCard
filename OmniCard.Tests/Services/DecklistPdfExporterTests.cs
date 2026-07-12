using OmniCard.Audit;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class DecklistPdfExporterTests : IDisposable
{
    private readonly string _tempDir;

    public DecklistPdfExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

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

        var exporter = new DecklistPdfExporter(null!);
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

    [Fact]
    public void Export_EmptyResult_CreatesValidPdf()
    {
        var result = new DecklistCheckResult
        {
            DeckName = "Empty Deck",
            DeckSource = "Text",
            OwnedEntries = [],
            MissingEntries = [],
        };

        var exporter = new DecklistPdfExporter(null!);
        var path = Path.Combine(_tempDir, "empty_report.pdf");
        exporter.Export(result, path);

        Assert.True(File.Exists(path));
    }

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

        var exporter = new DecklistPdfExporter(null!);
        var path = Path.Combine(_tempDir, "detail_report.pdf");
        exporter.ExportDetailed(result, path);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
    }
}
