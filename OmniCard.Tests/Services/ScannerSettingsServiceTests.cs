using System.IO;
using OmniCard.Collection;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class ScannerSettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ScannerSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private IScannerSettingsService CreateService() => new ScannerSettingsService(new StubDataPathService(_tempDir));

    [Fact]
    public void WorkflowMode_DefaultsToOriginal_WhenNoFileExists()
    {
        var service = CreateService();
        Assert.Equal(ScanWorkflowMode.Store, service.WorkflowMode);
    }

    [Fact]
    public void SetWorkflowMode_RoundTrips()
    {
        var service = CreateService();
        service.SetWorkflowMode(ScanWorkflowMode.Discard);

        var reloaded = CreateService();
        Assert.Equal(ScanWorkflowMode.Discard, reloaded.WorkflowMode);
    }

    [Fact]
    public void Load_ToleratesCorruptJson_FallsBackToDefault()
    {
        File.WriteAllText(Path.Combine(_tempDir, "scanner-settings.json"), "{ not valid json");

        var service = CreateService();
        Assert.Equal(ScanWorkflowMode.Store, service.WorkflowMode);
    }

    private class StubDataPathService(string dataDirectory) : IDataPathService
    {
        public string DataDirectory => dataDirectory;
        public string ScansDirectory => Path.Combine(dataDirectory, "scans");
        public string TempScansDirectory => Path.Combine(dataDirectory, "temp_scans");
        public string SymbolsCacheDirectory => Path.Combine(dataDirectory, "symbols", "sets");
        public string LogsDirectory => Path.Combine(dataDirectory, "logs");
        public string TradesDirectory => Path.Combine(dataDirectory, "trades");
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
