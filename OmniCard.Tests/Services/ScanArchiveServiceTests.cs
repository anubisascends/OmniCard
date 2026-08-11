using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class ScanArchiveServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;
    private readonly string _tempDir;
    private readonly StubDataPathService _dataPath;

    public ScanArchiveServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_connection).Options;
        using var ctx = new OmniCardDbContext(_options);
        ctx.Database.EnsureCreated();

        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _dataPath = new StubDataPathService(_tempDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    private IScanArchiveService CreateService() =>
        new ScanArchiveService(_dataPath, new MockOmniDbContextFactory(_options), NullLogger<ScanArchiveService>.Instance);

    private int SeedLot(string name = "Lightning Bolt")
    {
        using var ctx = new OmniCardDbContext(_options);
        var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = name };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = product.Id };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    private void WriteFakeScanImage(int lotId)
    {
        Directory.CreateDirectory(_dataPath.ScansDirectory);
        File.WriteAllBytes(Path.Combine(_dataPath.ScansDirectory, $"{lotId}.jpg"), [0xFF, 0xD8, 0xFF]);

        // Mirrors what CardService.CommitScans sets in Store mode — needed so archive tests can
        // verify ScanImagePath actually gets cleared, not just that it started null.
        using var ctx = new OmniCardDbContext(_options);
        var lot = ctx.Lots.Single(l => l.Id == lotId);
        lot.ScanImagePath = $"scans/{lotId}.jpg";
        ctx.SaveChanges();
    }

    [Fact]
    public async Task ArchiveCurrentScansAsync_EmptyScansDirectory_ReturnsSuccessWithZeroImages()
    {
        var service = CreateService();
        var result = await service.ArchiveCurrentScansAsync();

        Assert.True(result.Success);
        Assert.Equal(0, result.ImageCount);
        Assert.Null(result.ArchivePath);
    }

    [Fact]
    public async Task ArchiveCurrentScansAsync_ZipsImagesAndMapping_MappingMatchesLotIds()
    {
        var lotId1 = SeedLot("Lightning Bolt");
        var lotId2 = SeedLot("Counterspell");
        WriteFakeScanImage(lotId1);
        WriteFakeScanImage(lotId2);

        var service = CreateService();
        var result = await service.ArchiveCurrentScansAsync();

        Assert.True(result.Success);
        Assert.Equal(2, result.ImageCount);
        Assert.NotNull(result.ArchivePath);
        Assert.True(File.Exists(result.ArchivePath));

        using var archive = ZipFile.OpenRead(result.ArchivePath!);
        Assert.Equal(3, archive.Entries.Count); // 2 images + mapping

        var mappingEntry = archive.GetEntry("scan_mapping.json");
        Assert.NotNull(mappingEntry);
        using var stream = mappingEntry!.Open();
        var mapping = JsonSerializer.Deserialize<List<ScanMappingEntry>>(stream, new JsonSerializerOptions { WriteIndented = true })!;

        Assert.Equal(2, mapping.Count);
        Assert.Contains(mapping, m => m.LotId == lotId1 && m.FileName == $"{lotId1}.jpg" && m.ProductName == "Lightning Bolt");
        Assert.Contains(mapping, m => m.LotId == lotId2 && m.FileName == $"{lotId2}.jpg" && m.ProductName == "Counterspell");

        // The archived source files must be removed from the scans directory, and the DB must
        // no longer claim to have a scan image for either lot (the file is gone).
        Assert.False(File.Exists(Path.Combine(_dataPath.ScansDirectory, $"{lotId1}.jpg")));
        Assert.False(File.Exists(Path.Combine(_dataPath.ScansDirectory, $"{lotId2}.jpg")));

        using var ctx = new OmniCardDbContext(_options);
        Assert.Null(ctx.Lots.Single(l => l.Id == lotId1).ScanImagePath);
        Assert.Null(ctx.Lots.Single(l => l.Id == lotId2).ScanImagePath);
    }

    [Fact]
    public async Task ArchiveCurrentScansAsync_ManyScans_DoesNotHitSqlVariableLimit()
    {
        // Regression test: SQLite caps host parameters per statement, so building the mapping
        // with a single `WHERE Id IN (...)` over a large lot-id list throws "too many SQL
        // variables". BuildMapping must chunk the lookup (via CardService.ChunkedByIdLookup).
        const int lotCount = 1200; // comfortably past SqlInParameterChunkSize (500) and SQLite's default cap
        for (var i = 0; i < lotCount; i++)
            WriteFakeScanImage(SeedLot($"Card {i}"));

        var service = CreateService();
        var result = await service.ArchiveCurrentScansAsync();

        Assert.True(result.Success);
        Assert.Equal(lotCount, result.ImageCount);
    }

    [Fact]
    public async Task ArchiveCurrentScansAsync_LockedFile_ReturnsFriendlyError_DoesNotThrow()
    {
        var lotId = SeedLot();
        WriteFakeScanImage(lotId);
        using var lockHandle = new FileStream(
            Path.Combine(_dataPath.ScansDirectory, $"{lotId}.jpg"),
            FileMode.Open, FileAccess.Read, FileShare.None);

        var service = CreateService();
        var result = await service.ArchiveCurrentScansAsync();

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ImportArchiveAsync_ExtractsImages_LinksExistingLots()
    {
        var lotId = SeedLot();
        WriteFakeScanImage(lotId);
        var service = CreateService();
        // Archiving deletes the source file and clears ScanImagePath itself (see the
        // ArchiveCurrentScansAsync cleanup test), so the scans dir is already empty here.
        var archiveResult = await service.ArchiveCurrentScansAsync();

        var restoreResult = await service.ImportArchiveAsync(archiveResult.ArchivePath!);

        Assert.True(restoreResult.Success);
        Assert.Equal(1, restoreResult.ImagesExtracted);
        Assert.Equal(1, restoreResult.LinkedToLots);
        Assert.Equal(0, restoreResult.Orphaned);
        Assert.True(File.Exists(Path.Combine(_dataPath.ScansDirectory, $"{lotId}.jpg")));

        using var ctx = new OmniCardDbContext(_options);
        var lot = ctx.Lots.Single(l => l.Id == lotId);
        Assert.Equal($"scans/{lotId}.jpg", lot.ScanImagePath);
    }

    [Fact]
    public async Task ImportArchiveAsync_OrphanedLot_ExtractsFileButSkipsDbLink_ReportsOrphan()
    {
        var lotId = SeedLot();
        WriteFakeScanImage(lotId);
        var service = CreateService();
        var archiveResult = await service.ArchiveCurrentScansAsync();

        // Delete the lot from the DB to simulate it being removed since the archive was made
        using (var ctx = new OmniCardDbContext(_options))
        {
            var lot = ctx.Lots.Single(l => l.Id == lotId);
            ctx.Lots.Remove(lot);
            ctx.SaveChanges();
        }

        var restoreResult = await service.ImportArchiveAsync(archiveResult.ArchivePath!);

        Assert.True(restoreResult.Success);
        Assert.Equal(1, restoreResult.ImagesExtracted);
        Assert.Equal(0, restoreResult.LinkedToLots);
        Assert.Equal(1, restoreResult.Orphaned);
        Assert.Contains($"{lotId}.jpg", restoreResult.OrphanedFileNames);
        Assert.True(File.Exists(Path.Combine(_dataPath.ScansDirectory, $"{lotId}.jpg")));
    }

    [Fact]
    public async Task ImportArchiveAsync_CorruptZip_ReturnsFriendlyError()
    {
        var badZipPath = Path.Combine(_tempDir, "not-a-zip.zip");
        File.WriteAllText(badZipPath, "this is not a zip file");

        var service = CreateService();
        var result = await service.ImportArchiveAsync(badZipPath);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ImportArchiveAsync_MissingMappingJson_TreatsAllAsOrphaned()
    {
        var zipPath = Path.Combine(_tempDir, "no-mapping.zip");
        using (var fileStream = new FileStream(zipPath, FileMode.CreateNew))
        using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("999.jpg");
            using var entryStream = entry.Open();
            entryStream.Write([0xFF, 0xD8, 0xFF]);
        }

        var service = CreateService();
        var result = await service.ImportArchiveAsync(zipPath);

        Assert.True(result.Success);
        Assert.Equal(1, result.ImagesExtracted);
        Assert.Equal(1, result.Orphaned);
        Assert.Equal(0, result.LinkedToLots);
    }

    private class MockOmniDbContextFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
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
