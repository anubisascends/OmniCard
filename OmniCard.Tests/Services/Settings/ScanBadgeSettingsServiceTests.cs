using OmniCard.Collection.Settings;
using OmniCard.Shared.Settings;
using Xunit;

namespace OmniCard.Tests.Services.Settings;

public sealed class ScanBadgeSettingsServiceTests : IDisposable
{
    private readonly string _dir;

    public ScanBadgeSettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "omnicard-scanbadge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    private ScanBadgeSettingsService NewService() => new(new DataPath(_dir));

    [Fact]
    public void Get_WithNoSavedFile_ReturnsDefaults()
    {
        var s = NewService().Get();
        Assert.Equal(ScanBadgeSettings.DefaultCurrencyCode, s.CurrencyCode);
        Assert.Equal(ScanBadgeSettings.DefaultThresholds, s.Thresholds);
    }

    [Fact]
    public void Save_RoundTrips_NormalizedCurrencyAndSortedThresholds()
    {
        NewService().Save("eur", [50m, 5m, 20m, 1m]); // out of order, lower-case code
        var s = NewService().Get();
        Assert.Equal("EUR", s.CurrencyCode);
        Assert.Equal([1m, 5m, 20m, 50m], s.Thresholds);
    }

    [Fact]
    public void Save_DropsNonPositive_AndPadsFromDefaults()
    {
        // Only two usable thresholds survive; the rest are padded from the defaults to keep four.
        NewService().Save("USD", [0m, -3m, 2m, 8m]);
        var s = NewService().Get();
        Assert.Equal(ScanBadgeSettings.ThresholdCount, s.Thresholds.Count);
        Assert.Equal(2m, s.Thresholds[0]);
        Assert.Equal(8m, s.Thresholds[1]);
        // Ascending overall.
        for (var i = 1; i < s.Thresholds.Count; i++)
            Assert.True(s.Thresholds[i] >= s.Thresholds[i - 1]);
    }

    [Fact]
    public void Save_TruncatesExtraThresholds_ToFour()
    {
        NewService().Save("USD", [1m, 2m, 3m, 4m, 5m, 6m]);
        var s = NewService().Get();
        Assert.Equal([1m, 2m, 3m, 4m], s.Thresholds);
    }

    [Fact]
    public void Save_EmptyCurrency_FallsBackToDefault()
    {
        NewService().Save("", ScanBadgeSettings.DefaultThresholds);
        Assert.Equal(ScanBadgeSettings.DefaultCurrencyCode, NewService().Get().CurrencyCode);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private sealed class DataPath(string dir) : IDataPathService
    {
        public string DataDirectory => dir;
        public string ScansDirectory => Path.Combine(dir, "scans");
        public string TempScansDirectory => Path.Combine(dir, "temp_scans");
        public string SymbolsCacheDirectory => Path.Combine(dir, "symbols", "sets");
        public string LogsDirectory => Path.Combine(dir, "logs");
        public string TradesDirectory => Path.Combine(dir, "trades");
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
