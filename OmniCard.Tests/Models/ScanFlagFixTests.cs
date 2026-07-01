using System.Text.Json;
using OmniCard.Models;

namespace OmniCard.Tests.Models;

public class ScanFlagFixTests
{
    [Fact]
    public void FlagFix_DefaultsToNull()
    {
        var card = new ScannedCard
        {
            TempImagePath = "/tmp/test.png",
            Hash = 0x1234
        };

        Assert.Null(card.FlagFix);
    }

    [Fact]
    public void ScanFlagFix_SerializesToJson()
    {
        var fix = new ScanFlagFix
        {
            FixType = "PrintingChange",
            OriginalData = JsonSerializer.Serialize(new { name = "Lightning Bolt", setCode = "m10" }),
            ResolvedData = JsonSerializer.Serialize(new { name = "Lightning Bolt", setCode = "lea" }),
        };

        Assert.Equal("PrintingChange", fix.FixType);
        Assert.Contains("m10", fix.OriginalData);
        Assert.Contains("lea", fix.ResolvedData);
    }
}
