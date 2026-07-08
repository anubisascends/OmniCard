using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Interfaces;
using OmniCard.Data;

namespace OmniCard.Tests.Services;

public class DataMigrationServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _sourceDir;
    private readonly string _targetDir;
    private readonly string _configDir;

    public DataMigrationServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"migration-test-{Guid.NewGuid()}");
        _sourceDir = Path.Combine(_testRoot, "source");
        _targetDir = Path.Combine(_testRoot, "target");
        _configDir = Path.Combine(_testRoot, "config");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_configDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, true);
    }

    private (DataPathService pathService, DataMigrationService migrationService) CreateServices()
    {
        // Write config pointing to sourceDir, with pending pointing to targetDir
        var configPath = Path.Combine(_configDir, "datapath.json");
        File.WriteAllText(configPath, $$"""
            {
                "dataDirectory": "{{_sourceDir.Replace("\\", "\\\\")}}",
                "pendingDataDirectory": "{{_targetDir.Replace("\\", "\\\\")}}"
            }
            """);

        var pathService = new DataPathService(_configDir);
        var migrationService = new DataMigrationService(pathService, NullLogger<DataMigrationService>.Instance);
        return (pathService, migrationService);
    }

    private void SeedSourceFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_sourceDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [Fact]
    public async Task PrepareMigration_CountsFilesAndBytes()
    {
        SeedSourceFile("collection.db", "fake-db-content-12345");
        SeedSourceFile("scans/card1.jpg", "image-data-1");
        SeedSourceFile("scans/card2.jpg", "image-data-2");
        var (_, service) = CreateServices();

        var plan = await service.PrepareMigrationAsync();

        Assert.Equal(3, plan.FileCount);
        Assert.True(plan.TotalBytes > 0);
    }

    [Fact]
    public async Task ExecuteMigration_CopiesAndVerifiesFiles()
    {
        SeedSourceFile("collection.db", "database-content");
        SeedSourceFile("scans/card1.jpg", "image-bytes");
        SeedSourceFile("collection-presets.json", "{}");
        var (pathService, service) = CreateServices();

        var result = await service.ExecuteMigrationAsync(
            new Progress<MigrationProgress>(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_targetDir, "collection.db")));
        Assert.True(File.Exists(Path.Combine(_targetDir, "scans", "card1.jpg")));
        Assert.True(File.Exists(Path.Combine(_targetDir, "collection-presets.json")));
    }

    [Fact]
    public async Task ExecuteMigration_CommitsPathService()
    {
        SeedSourceFile("collection.db", "data");
        var (pathService, service) = CreateServices();

        await service.ExecuteMigrationAsync(
            new Progress<MigrationProgress>(), CancellationToken.None);

        Assert.Equal(_targetDir, pathService.DataDirectory);
        Assert.False(pathService.IsMigrationPending);
    }

    [Fact]
    public async Task ExecuteMigration_DeletesSourceFiles()
    {
        SeedSourceFile("collection.db", "data");
        SeedSourceFile("scans/card.jpg", "img");
        var (_, service) = CreateServices();

        await service.ExecuteMigrationAsync(
            new Progress<MigrationProgress>(), CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_sourceDir, "collection.db")));
        Assert.False(File.Exists(Path.Combine(_sourceDir, "scans", "card.jpg")));
    }

    [Fact]
    public async Task ExecuteMigration_VerifiesChecksums()
    {
        var content = "important-database-data-with-specific-hash";
        SeedSourceFile("scryfall.db", content);
        var (_, service) = CreateServices();

        var result = await service.ExecuteMigrationAsync(
            new Progress<MigrationProgress>(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(content, File.ReadAllText(Path.Combine(_targetDir, "scryfall.db")));
    }

    [Fact]
    public async Task ExecuteMigration_ReportsProgress()
    {
        SeedSourceFile("collection.db", "data1");
        SeedSourceFile("scryfall.db", "data2");
        var (_, service) = CreateServices();
        var reports = new List<MigrationProgress>();
        var progress = new Progress<MigrationProgress>(p => reports.Add(p));

        await service.ExecuteMigrationAsync(progress, CancellationToken.None);

        // Allow progress callbacks to fire (they're posted to sync context)
        await Task.Delay(100);
        Assert.True(reports.Count > 0);
    }

    [Fact]
    public async Task ExecuteMigration_Cancellation_CleansUpTarget()
    {
        SeedSourceFile("collection.db", "data");
        SeedSourceFile("scryfall.db", "data");
        SeedSourceFile("optcg.db", "data");
        var (pathService, service) = CreateServices();
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var result = await service.ExecuteMigrationAsync(
            new Progress<MigrationProgress>(), cts.Token);

        Assert.False(result.Success);
        // Source should be untouched
        Assert.True(File.Exists(Path.Combine(_sourceDir, "collection.db")));
        // Pending should still be set
        Assert.True(pathService.IsMigrationPending);
    }

    [Fact]
    public async Task ExecuteMigration_NoPending_ReturnsFailure()
    {
        // Config with no pending directory
        File.WriteAllText(Path.Combine(_configDir, "datapath.json"), $$"""
            {
                "dataDirectory": "{{_sourceDir.Replace("\\", "\\\\")}}"
            }
            """);
        var pathService = new DataPathService(_configDir);
        var service = new DataMigrationService(pathService, NullLogger<DataMigrationService>.Instance);

        var result = await service.ExecuteMigrationAsync(
            new Progress<MigrationProgress>(), CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteMigration_EmptySource_SucceedsWithNoFiles()
    {
        // Source exists but has none of the known files
        var (_, service) = CreateServices();

        var plan = await service.PrepareMigrationAsync();
        Assert.Equal(0, plan.FileCount);

        var result = await service.ExecuteMigrationAsync(
            new Progress<MigrationProgress>(), CancellationToken.None);
        Assert.True(result.Success);
    }
}
