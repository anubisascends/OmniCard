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
                ])
            ],
            MissingEntries =
            [
                new MissingDecklistEntry("Ragavan, Nimble Pilferer", "MH2", "138", 1, 55.00m)
            ],
        };

        var exporter = new DecklistPdfExporter();
        var path = Path.Combine(_tempDir, "test_report.pdf");
        exporter.Export(result, path);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 100);
        // PDF magic bytes
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

        var exporter = new DecklistPdfExporter();
        var path = Path.Combine(_tempDir, "empty_report.pdf");
        exporter.Export(result, path);

        Assert.True(File.Exists(path));
    }
}
