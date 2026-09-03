using System.Text.Json;
using OmniCard.Data;

namespace OmniCard.Tests.Services;

public class DataPathServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public DataPathServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"datapath-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "datapath.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // Use the temp dir as BOTH the config base and the local-app-data root, so the OmniCard/
    // TCGCardScanner folder probing is hermetic and never touches the real %LOCALAPPDATA%.
    private DataPathService CreateService() => new(_tempDir, _tempDir);

    private string DefaultDir => Path.Combine(_tempDir, "OmniCard");
    private string LegacyDir => Path.Combine(_tempDir, "TCGCardScanner");

    [Fact]
    public void NoConfig_NeitherFolderExists_DefaultsToOmniCard()
    {
        var service = CreateService();
        Assert.Equal(DefaultDir, service.DataDirectory);
    }

    [Fact]
    public void NoConfig_LegacyFolderExists_UsesLegacy()
    {
        Directory.CreateDirectory(LegacyDir);

        var service = CreateService();
        Assert.Equal(LegacyDir, service.DataDirectory);
    }

    [Fact]
    public void NoConfig_LegacyAndDefaultBothExist_PrefersLegacy()
    {
        // The OmniCard folder is always created early (for settings), so legacy must still win
        // when both are present.
        Directory.CreateDirectory(LegacyDir);
        Directory.CreateDirectory(DefaultDir);

        var service = CreateService();
        Assert.Equal(LegacyDir, service.DataDirectory);
    }

    [Fact]
    public void ConfigWithCustomPath_IgnoresLegacyFolder()
    {
        // An explicit user choice wins even if a legacy folder is lying around.
        Directory.CreateDirectory(LegacyDir);
        var customPath = Path.Combine(_tempDir, "custom-data");
        File.WriteAllText(_configPath, JsonSerializer.Serialize(new { dataDirectory = customPath }));

        var service = CreateService();
        Assert.Equal(customPath, service.DataDirectory);
    }

    [Fact]
    public void ConfigFileExists_UsesConfiguredPath()
    {
        var customPath = Path.Combine(_tempDir, "custom-data");
        File.WriteAllText(_configPath, JsonSerializer.Serialize(new { dataDirectory = customPath }));

        var service = CreateService();
        Assert.Equal(customPath, service.DataDirectory);
    }

    [Fact]
    public void DerivedPaths_CorrectlyBuilt()
    {
        var service = CreateService();
        var dd = service.DataDirectory;
        Assert.Equal(Path.Combine(dd, "scans"), service.ScansDirectory);
        Assert.Equal(Path.Combine(dd, "temp_scans"), service.TempScansDirectory);
        Assert.Equal(Path.Combine(dd, "symbols", "sets"), service.SymbolsCacheDirectory);
        Assert.Equal(Path.Combine(dd, "logs"), service.LogsDirectory);
    }

    [Fact]
    public void SetPendingDataDirectory_StoresPending()
    {
        var service = CreateService();
        service.SetPendingDataDirectory(@"D:\NewPath");

        Assert.True(service.IsMigrationPending);
        Assert.Equal(@"D:\NewPath", service.PendingDataDirectory);
    }

    [Fact]
    public void SetPendingDataDirectory_PersistsToFile()
    {
        var service = CreateService();
        service.SetPendingDataDirectory(@"D:\NewPath");

        var reloaded = CreateService();
        Assert.Equal(@"D:\NewPath", reloaded.PendingDataDirectory);
    }

    [Fact]
    public void CommitMigration_SwapsPendingToActive()
    {
        var service = CreateService();
        service.SetPendingDataDirectory(@"D:\NewPath");
        service.CommitMigration();

        Assert.False(service.IsMigrationPending);
        Assert.Null(service.PendingDataDirectory);
        Assert.Equal(@"D:\NewPath", service.DataDirectory);
    }

    [Fact]
    public void CommitMigration_PersistsToFile()
    {
        var service = CreateService();
        service.SetPendingDataDirectory(@"D:\NewPath");
        service.CommitMigration();

        var reloaded = CreateService();
        Assert.Equal(@"D:\NewPath", reloaded.DataDirectory);
        Assert.False(reloaded.IsMigrationPending);
    }

    [Fact]
    public void CancelPendingMigration_ClearsPending()
    {
        var service = CreateService();
        service.SetPendingDataDirectory(@"D:\NewPath");
        service.CancelPendingMigration();

        Assert.False(service.IsMigrationPending);
        Assert.Null(service.PendingDataDirectory);
    }

    [Fact]
    public void CommitMigration_NoPending_ThrowsInvalidOperation()
    {
        var service = CreateService();
        Assert.Throws<InvalidOperationException>(() => service.CommitMigration());
    }
}
