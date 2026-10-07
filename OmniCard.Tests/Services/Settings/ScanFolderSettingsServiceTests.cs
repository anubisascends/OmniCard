using OmniCard.Collection.Settings;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Settings;
using Xunit;

namespace OmniCard.Tests.Services.Settings;

public sealed class ScanFolderSettingsServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _dataDir;

    public ScanFolderSettingsServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "omnicard-scanfolders-" + Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(_root, "data");
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private ScanFolderSettingsService NewService() => new(new DataPath(_dataDir));

    private string Scans(string name) => Path.Combine(_root, "scans", name);

    [Fact]
    public void Get_WithNoSavedFile_ReturnsDisabledDefaults()
    {
        var s = NewService().Get();
        Assert.False(s.Enabled);
        Assert.Equal(ScanFolderSettings.DefaultQuietPeriodSeconds, s.QuietPeriodSeconds);
        Assert.Equal(ScanFolderSettings.DefaultRetentionDays, s.RetentionDays);
        Assert.Empty(s.Folders);
    }

    [Fact]
    public void Save_RoundTrips_AndRaisesChanged()
    {
        var svc = NewService();
        var raised = 0;
        svc.Changed += () => raised++;

        var errors = svc.Save(new ScanFolderSettings
        {
            Enabled = true,
            QuietPeriodSeconds = 120,
            Folders =
            [
                new() { Game = CardGame.Mtg, Path = Scans("Mtg"), IsFoil = true, Language = "JP", SetCodes = ["neo", " NEO ", ""], DefaultContainerId = 7 },
                new() { Game = CardGame.Pokemon, Path = Scans("Pokemon"), Condition = "" },
            ],
        });

        Assert.Empty(errors);
        Assert.Equal(1, raised);
        var s = NewService().Get();
        Assert.True(s.Enabled);
        Assert.Equal(120, s.QuietPeriodSeconds);
        var mtg = Assert.Single(s.Folders, f => f.Game == CardGame.Mtg);
        Assert.True(mtg.IsFoil);
        Assert.Equal("ja", mtg.Language);
        Assert.Equal(["neo"], mtg.SetCodes);
        Assert.Equal(7, mtg.DefaultContainerId);
        Assert.Equal("NM", s.Folders.Single(f => f.Game == CardGame.Pokemon).Condition);
    }

    [Fact]
    public void Save_ClampsNumbers_AndDropsBlankPaths()
    {
        NewService().Save(new ScanFolderSettings
        {
            QuietPeriodSeconds = 1,
            RetentionDays = 10_000,
            Folders = [new() { Game = CardGame.Mtg, Path = "   " }],
        });
        var s = NewService().Get();
        Assert.Equal(ScanFolderSettings.MinQuietPeriodSeconds, s.QuietPeriodSeconds);
        Assert.Equal(ScanFolderSettings.MaxRetentionDays, s.RetentionDays);
        Assert.Empty(s.Folders);
    }

    [Fact]
    public void Save_RejectsDuplicateGame()
    {
        var errors = NewService().Save(new ScanFolderSettings
        {
            Folders = [new() { Game = CardGame.Mtg, Path = Scans("a") }, new() { Game = CardGame.Mtg, Path = Scans("b") }],
        });
        Assert.NotEmpty(errors);
        Assert.Empty(NewService().Get().Folders); // nothing saved
    }

    [Fact]
    public void Save_RejectsRelativePath()
    {
        var errors = NewService().Save(new ScanFolderSettings
        {
            Folders = [new() { Game = CardGame.Mtg, Path = @"scans\mtg" }],
        });
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Save_RejectsOverlappingFolders()
    {
        var errors = NewService().Save(new ScanFolderSettings
        {
            Folders =
            [
                new() { Game = CardGame.Mtg, Path = Scans("all") },
                new() { Game = CardGame.Pokemon, Path = Path.Combine(Scans("all"), "pokemon") },
            ],
        });
        Assert.Contains(errors, e => e.Contains("overlap"));
    }

    [Fact]
    public void Save_AllowsSiblingFoldersWithSharedPrefix()
    {
        // "Mtg" and "Mtg2" share a string prefix but are not nested.
        var errors = NewService().Save(new ScanFolderSettings
        {
            Folders =
            [
                new() { Game = CardGame.Mtg, Path = Scans("Mtg") },
                new() { Game = CardGame.Pokemon, Path = Scans("Mtg2") },
            ],
        });
        Assert.Empty(errors);
    }

    [Fact]
    public void Save_RejectsFolderInsideDataDirectory()
    {
        var errors = NewService().Save(new ScanFolderSettings
        {
            Folders = [new() { Game = CardGame.Mtg, Path = Path.Combine(_dataDir, "incoming") }],
        });
        Assert.Contains(errors, e => e.Contains("data directory"));
    }

    [Fact]
    public void Get_WithCorruptFile_FallsBackToDefaults()
    {
        File.WriteAllText(Path.Combine(_dataDir, "scan-folder-settings.json"), "{ not json");
        var s = NewService().Get();
        Assert.False(s.Enabled);
        Assert.Empty(s.Folders);
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
