using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class ScanArchiveService : IScanArchiveService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const string MappingEntryName = "scan_mapping.json";

    private readonly IDataPathService _dataPathService;
    private readonly IDbContextFactory<OmniCardDbContext> _omniDbContextFactory;
    private readonly ILogger<ScanArchiveService> _logger;

    public ScanArchiveService(
        IDataPathService dataPathService,
        IDbContextFactory<OmniCardDbContext> omniDbContextFactory,
        ILogger<ScanArchiveService> logger)
    {
        _dataPathService = dataPathService;
        _omniDbContextFactory = omniDbContextFactory;
        _logger = logger;
    }

    public Task<ScanArchiveResult> ArchiveCurrentScansAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        => Task.Run(() => ArchiveCurrentScans(progress, ct), ct);

    public Task<ScanRestoreResult> ImportArchiveAsync(string zipPath, IProgress<string>? progress = null, CancellationToken ct = default)
        => Task.Run(() => ImportArchive(zipPath, progress, ct), ct);

    private ScanArchiveResult ArchiveCurrentScans(IProgress<string>? progress, CancellationToken ct)
    {
        try
        {
            var scansDir = _dataPathService.ScansDirectory;
            var files = Directory.Exists(scansDir) ? Directory.GetFiles(scansDir, "*.jpg") : [];
            if (files.Length == 0)
                return new ScanArchiveResult { Success = true, ImageCount = 0 };

            progress?.Report($"Archiving {files.Length} scans...");

            var archivesDir = Path.Combine(_dataPathService.DataDirectory, "archives");
            Directory.CreateDirectory(archivesDir);
            var archivePath = Path.Combine(archivesDir, $"scans-archive-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

            var mapping = BuildMapping(files);

            using (var fileStream = new FileStream(archivePath, FileMode.CreateNew))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
            {
                var count = 0;
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    archive.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
                    count++;
                    if (count % 10 == 0 || count == files.Length)
                        progress?.Report($"Archiving scans... {count}/{files.Length}");
                }

                var mappingEntry = archive.CreateEntry(MappingEntryName, CompressionLevel.Optimal);
                using var mappingStream = mappingEntry.Open();
                JsonSerializer.Serialize(mappingStream, mapping, JsonOptions);
            }

            // The archive is now the durable copy — clear ScanImagePath for the lots we just
            // archived (their file is about to be deleted) before removing the source files, so
            // the DB never points at a scan image that no longer exists on disk.
            if (mapping.Count > 0)
            {
                using var context = _omniDbContextFactory.CreateDbContext();
                using var transaction = context.Database.BeginTransaction();
                try
                {
                    var lotIds = mapping.Select(m => m.LotId).ToList();
                    var lots = CardService.ChunkedByIdLookup(
                        lotIds,
                        chunk => context.Lots.Where(l => chunk.Contains(l.Id)).ToList(),
                        l => l.Id);

                    foreach (var lot in lots.Values)
                        lot.ScanImagePath = null;

                    context.SaveChanges();
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }

            progress?.Report("Removing archived scans...");
            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete scan file after archiving: {Path}", file);
                }
            }

            progress?.Report("Archive complete.");
            return new ScanArchiveResult { Success = true, ArchivePath = archivePath, ImageCount = files.Length };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive current scans");
            return new ScanArchiveResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private List<ScanMappingEntry> BuildMapping(string[] files)
    {
        using var context = _omniDbContextFactory.CreateDbContext();
        var lotIds = files
            .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        var lots = CardService.ChunkedByIdLookup(
            lotIds,
            chunk => context.Lots.Include(l => l.Product).Where(l => chunk.Contains(l.Id)).ToList(),
            l => l.Id);

        var archivedAt = DateTime.UtcNow;
        var mapping = new List<ScanMappingEntry>();
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out var lotId) || !lots.TryGetValue(lotId, out var lot))
                continue;

            mapping.Add(new ScanMappingEntry
            {
                FileName = fileName,
                LotId = lot.Id,
                ProductId = lot.ProductId,
                ProductName = lot.Product?.Name,
                ArchivedAt = archivedAt,
            });
        }

        return mapping;
    }

    private ScanRestoreResult ImportArchive(string zipPath, IProgress<string>? progress, CancellationToken ct)
    {
        try
        {
            var scansDir = _dataPathService.ScansDirectory;
            Directory.CreateDirectory(scansDir);

            progress?.Report("Reading archive...");
            using var archive = ZipFile.OpenRead(zipPath);

            var mapping = new List<ScanMappingEntry>();
            var mappingEntry = archive.GetEntry(MappingEntryName);
            if (mappingEntry is not null)
            {
                using var mappingStream = mappingEntry.Open();
                mapping = JsonSerializer.Deserialize<List<ScanMappingEntry>>(mappingStream, JsonOptions) ?? [];
            }

            var result = new ScanRestoreResult { Success = true };
            var imageEntries = archive.Entries.Where(e => e.Name != MappingEntryName && e.Name.Length > 0).ToList();
            var extracted = 0;
            foreach (var entry in imageEntries)
            {
                ct.ThrowIfCancellationRequested();
                var destPath = Path.Combine(scansDir, entry.Name);
                entry.ExtractToFile(destPath, overwrite: true);
                extracted++;
                if (extracted % 10 == 0 || extracted == imageEntries.Count)
                    progress?.Report($"Extracting scans... {extracted}/{imageEntries.Count}");
            }
            result.ImagesExtracted = extracted;

            if (mappingEntry is null)
            {
                result.Orphaned = extracted;
                result.OrphanedFileNames = imageEntries.Select(e => e.Name).ToList();
                result.ErrorMessage = "Archive had no scan_mapping.json — images were extracted but could not be relinked.";
                return result;
            }

            progress?.Report("Relinking scans to inventory...");
            using var context = _omniDbContextFactory.CreateDbContext();
            using var transaction = context.Database.BeginTransaction();
            try
            {
                foreach (var mappingItem in mapping)
                {
                    var lot = context.Lots.Find(mappingItem.LotId);
                    if (lot is null)
                    {
                        result.Orphaned++;
                        result.OrphanedFileNames.Add(mappingItem.FileName);
                        continue;
                    }

                    lot.ScanImagePath = $"scans/{mappingItem.FileName}";
                    result.LinkedToLots++;
                }

                context.SaveChanges();
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            progress?.Report("Import complete.");
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import scan archive {ZipPath}", zipPath);
            return new ScanRestoreResult { Success = false, ErrorMessage = ex.Message };
        }
    }
}
