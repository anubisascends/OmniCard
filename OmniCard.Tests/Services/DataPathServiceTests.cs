using System.Text.Json;
using OmniCard.Services;

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

    private DataPathService CreateService() => new(_tempDir);

    [Fact]
    public void NoConfigFile_DefaultsToLocalAppData()
    {
        var service = CreateService();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var defaultPath = Path.Combine(localAppData, "OmniCard");
        var legacyPath = Path.Combine(localAppData, "TCGCardScanner");

        // Falls back to legacy path if it exists and new default doesn't
        var expected = Directory.Exists(legacyPath) && !Directory.Exists(defaultPath)
            ? legacyPath
            : defaultPath;
        Assert.Equal(expected, service.DataDirectory);
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
