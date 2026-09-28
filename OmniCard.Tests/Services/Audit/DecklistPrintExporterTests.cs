using OmniCard.Audit.Exporters;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Storage;

namespace OmniCard.Tests.Services.Audit;

public class DecklistPrintExporterTests
{
    private static DecklistCheckResult SampleResult(bool populated) => new()
    {
        DeckName = "Mono-Red: Burn / Test",
        DeckSource = "pasted",
        OwnedEntries = populated
            ?
            [
                new OwnedDecklistEntry("Lightning Bolt", "M11", "149", 3, [], Picks:
                [
                    new DecklistPick(1, 10, "Binder A", ContainerType.Binder, 3, 5, "Reds", "M11", "149", false, "NM", 2, false),
                    new DecklistPick(2, 11, "Bulk", ContainerType.Bulk, null, null, null, "2ED", "162", true, "LP", 1, true),
                ]),
            ]
            : [],
        MissingEntries = populated
            ? [new MissingDecklistEntry("Goblin Guide", "ZEN", "126", 4, 2.50m), new MissingDecklistEntry("Unpriced", null, null, 1, null)]
            : [],
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExportPullList_WritesPdf(bool populated) =>
        AssertWritesPdf(p => new DecklistPrintExporter().ExportPullList(SampleResult(populated), p));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExportMissingList_WritesPdf(bool populated) =>
        AssertWritesPdf(p => new DecklistPrintExporter().ExportMissingList(SampleResult(populated), p));

    private static void AssertWritesPdf(Action<string> export)
    {
        var path = Path.Combine(Path.GetTempPath(), $"decklist-{Guid.NewGuid():N}.pdf");
        try
        {
            export(path);
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
