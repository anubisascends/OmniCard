using System.IO;
using OmniCard.Audit;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class PickListPdfExporterTests
{
    [Fact]
    public void Export_WritesNonEmptyPdf_WithMixedRows()
    {
        var entries = new List<PickListEntry>
        {
            new(1, "Lightning Bolt", "Alpha", "LEA", "NM", false, "Binder A", "Reds", 3, 5, 12.50m, 1),
            new(2, "Black Lotus", "Alpha", "LEA", "LP", true, "Vault", null, null, null, 9000m, 1),
            new(3, "Island", "Alpha", "", null, false, "Bulk", null, null, null, 0.10m, 4),
        };

        var path = Path.Combine(Path.GetTempPath(), $"picklist-{Guid.NewGuid():N}.pdf");
        try
        {
            new PickListPdfExporter().Export(entries, path);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Export_EmptyList_StillWritesPdf()
    {
        var path = Path.Combine(Path.GetTempPath(), $"picklist-{Guid.NewGuid():N}.pdf");
        try
        {
            new PickListPdfExporter().Export([], path);
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
