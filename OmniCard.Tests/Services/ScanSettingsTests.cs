using OmniCard.Models;
using OmniCard.Scanner;
using Xunit;

namespace OmniCard.Tests.Services;

/// <summary>
/// Covers the quality/foil -> DPI/brightness/contrast mapping shared by the in-process
/// ScannerService and the out-of-process ScannerHost. This is the logic that previously
/// diverged between the two scan paths (Fast/HighQuality resolving to different DPIs).
/// </summary>
public class ScanSettingsTests
{
    [Fact]
    public void Resolve_Fast_UsesFastDpi()
    {
        var s = ScanSettings.Resolve(ScanQuality.Fast, foil: false,
            fastDpi: 200, highQualityDpi: 0, foilBrightness: -200f, foilContrast: 333.3333f);

        Assert.Equal(200, s.Dpi);
        Assert.False(s.Foil);
    }

    [Fact]
    public void Resolve_HighQuality_UsesHighQualityDpi()
    {
        var s = ScanSettings.Resolve(ScanQuality.HighQuality, foil: false,
            fastDpi: 200, highQualityDpi: 600, foilBrightness: -200f, foilContrast: 333.3333f);

        Assert.Equal(600, s.Dpi);
    }

    [Fact]
    public void Resolve_HighQuality_ZeroDpi_MeansNativeDefault()
    {
        // 0 is the "use the scanner's native default resolution" sentinel the applier honors.
        var s = ScanSettings.Resolve(ScanQuality.HighQuality, foil: false,
            fastDpi: 200, highQualityDpi: 0, foilBrightness: -200f, foilContrast: 333.3333f);

        Assert.Equal(0, s.Dpi);
    }

    [Fact]
    public void Resolve_Foil_CarriesFoilTuningThrough()
    {
        var s = ScanSettings.Resolve(ScanQuality.Fast, foil: true,
            fastDpi: 300, highQualityDpi: 0, foilBrightness: -150f, foilContrast: 275f);

        Assert.True(s.Foil);
        Assert.Equal(-150f, s.FoilBrightness);
        Assert.Equal(275f, s.FoilContrast);
        Assert.Equal(300, s.Dpi);
    }

    [Fact]
    public void Defaults_MatchPreviouslyShippedValues()
    {
        // Guards against accidentally changing the out-of-the-box scan behavior.
        Assert.Equal(200, ScanSettings.DefaultFastDpi);
        Assert.Equal(0, ScanSettings.DefaultHighQualityDpi);
        Assert.Equal(-200f, ScanSettings.DefaultFoilBrightness);
        Assert.Equal(333.3333f, ScanSettings.DefaultFoilContrast);
    }
}
