using System;
using System.IO;
using Moq;
using OmniCard.Collection;
using OmniCard.Interfaces;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

/// <summary>
/// Covers per-scanner profile persistence: sanitized keying (network scanner names contain colons
/// and spaces), round-trip of tuning + capability overrides, per-scanner isolation, and graceful
/// defaults on a missing/corrupt file.
/// </summary>
public class ScannerProfileServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly ScannerProfileService _svc;

    public ScannerProfileServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "omnicard_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        var paths = new Mock<IDataPathService>();
        paths.SetupGet(p => p.DataDirectory).Returns(_dir);
        _svc = new ScannerProfileService(paths.Object);
    }

    [Fact]
    public void GetProfile_Missing_ReturnsEmptyWithSanitizedKey()
    {
        var p = _svc.GetProfile("DR-G2140 : D0000001 : 192.168.0.12");

        Assert.Equal("DR-G2140 : D0000001 : 192.168.0.12", p.ScannerName);
        Assert.DoesNotContain(':', p.ScannerKey); // colon (invalid in filenames) is sanitized
        Assert.Empty(p.Capabilities);
    }

    [Fact]
    public void SaveThenGet_RoundTripsTuningAndCaps()
    {
        var p = _svc.GetProfile("CANON RS40");
        p.FastDpi = 300;
        p.HighQualityDpi = 0;
        p.FoilBrightness = -150;
        p.FoilContrast = 275;
        p.Capabilities.Add(new ScannerCapabilitySetting { CapId = "ICapBrightness", ItemType = "Fix32", Value = "123" });
        _svc.SaveProfile(p);

        var again = _svc.GetProfile("CANON RS40");

        Assert.Equal(300, again.FastDpi);
        Assert.Equal(0, again.HighQualityDpi);
        Assert.Equal(-150, again.FoilBrightness);
        Assert.Equal(275, again.FoilContrast);
        var cap = Assert.Single(again.Capabilities);
        Assert.Equal("ICapBrightness", cap.CapId);
        Assert.Equal("Fix32", cap.ItemType);
        Assert.Equal("123", cap.Value);
    }

    [Fact]
    public void SaveProfile_IsKeyedPerScanner_AndDoesNotClobberOthers()
    {
        var a = _svc.GetProfile("Scanner A");
        a.FastDpi = 111;
        _svc.SaveProfile(a);

        var b = _svc.GetProfile("Scanner B");
        b.FastDpi = 222;
        _svc.SaveProfile(b);

        Assert.Equal(111, _svc.GetProfile("Scanner A").FastDpi);
        Assert.Equal(222, _svc.GetProfile("Scanner B").FastDpi);
    }

    [Fact]
    public void GetProfile_CorruptFile_ReturnsDefault()
    {
        File.WriteAllText(Path.Combine(_dir, "scanner-profiles.json"), "{ this is not valid json");

        var p = _svc.GetProfile("X");

        Assert.NotNull(p);
        Assert.Empty(p.Capabilities);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
