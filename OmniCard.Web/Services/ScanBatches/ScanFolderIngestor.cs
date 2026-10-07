using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Scanning;
using OmniCard.Shared.Settings;
using OmniCard.Web.Api.Controllers;

namespace OmniCard.Web.Services.ScanBatches;

/// <summary>
/// Picks up new scan images from the watched folders (one per game) and adds them to batches. Each
/// top-level subfolder is a batch; files in a game folder's root go to a batch named after today's
/// date. A file is taken in only once it's finished writing — its size and timestamp held steady for a
/// few seconds and it opens exclusively — then it's copied into the data directory and the original is
/// moved to <c>&lt;game folder&gt;\_processed\&lt;batch&gt;\</c>, so it's never picked up twice.
///
/// Not thread-safe: <see cref="ScanBatchHostedService"/> calls <see cref="RunOnce"/> from its single loop.
/// </summary>
public sealed class ScanFolderIngestor(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    ScanBatchStorage storage,
    TimeProvider clock,
    ILogger<ScanFolderIngestor> logger)
{
    public const string ProcessedFolderName = "_processed";

    /// <summary>How long a file's size and timestamp must hold steady before it's considered written.</summary>
    internal static readonly TimeSpan StableFor = TimeSpan.FromSeconds(3);

    private readonly record struct Sighting(long Size, DateTime LastWriteUtc, DateTimeOffset Since);

    // Files seen but not yet taken in (still changing, or locked by the scanner software).
    private readonly Dictionary<string, Sighting> _sightings = new(StringComparer.OrdinalIgnoreCase);

    // Files copied into a batch whose move to _processed failed: source path → destination. Retried
    // each run, and skipped by enumeration so they aren't taken in twice.
    private readonly Dictionary<string, string> _pendingMoves = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<CardGame, string> _folderErrors = new();

    /// <summary>True while files are waiting to settle or be moved — the caller should run again soon.</summary>
    public bool HasUnsettledFiles => _sightings.Count > 0 || _pendingMoves.Count > 0;

    /// <summary>The last problem hit in each game's folder (missing, unreadable, move failed), for the
    /// settings page. Cleared once a run of that folder succeeds.</summary>
    public IReadOnlyDictionary<CardGame, string> FolderErrors => _folderErrors;

    /// <summary>Scan every enabled folder once; returns the number of files taken in.</summary>
    public int RunOnce(ScanFolderSettings settings)
    {
        RetryPendingMoves();

        var ingested = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in settings.Folders.Where(f => f.Enabled))
        {
            try
            {
                if (!Directory.Exists(folder.Path))
                {
                    _folderErrors[folder.Game] = $"Folder not found: {folder.Path}";
                    continue;
                }
                var moveFailed = false;
                foreach (var (file, folderKey) in EnumerateCandidates(folder.Path))
                {
                    if (_pendingMoves.ContainsKey(file))
                        continue;
                    seen.Add(file);
                    if (!IsSettled(file))
                        continue;
                    _sightings.Remove(file);
                    switch (Ingest(folder, file, folderKey ?? DateKey()))
                    {
                        case IngestResult.Ingested: ingested++; break;
                        case IngestResult.IngestedMoveFailed: ingested++; moveFailed = true; break;
                    }
                }
                if (!moveFailed)
                    _folderErrors.TryRemove(folder.Game, out _);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _folderErrors[folder.Game] = ex.Message;
                logger.LogWarning(ex, "Couldn't read the {Game} scan folder {Path}", folder.Game, folder.Path);
            }
        }

        // Forget files that disappeared (deleted or renamed by the user) before they settled.
        foreach (var gone in _sightings.Keys.Where(k => !seen.Contains(k)).ToList())
            _sightings.Remove(gone);

        if (ingested > 0)
            logger.LogInformation("Picked up {Count} scan image(s) from watched folders", ingested);
        return ingested;
    }

    private string DateKey() => clock.GetLocalNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Image files under <paramref name="root"/>, each with its batch key: the top-level
    /// subfolder name, or null for files in the root. Skips <c>_*</c> and <c>.*</c> folders (which
    /// covers <c>_processed</c>) and hidden/system/temporary files.</summary>
    internal static IEnumerable<(string File, string? FolderKey)> EnumerateCandidates(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        foreach (var file in Directory.EnumerateFiles(root, "*", options).Where(IsCandidateFile))
            yield return (file, null);

        foreach (var dir in Directory.EnumerateDirectories(root, "*", options).Where(IsCandidateDirectory))
        {
            var key = Path.GetFileName(dir);
            foreach (var file in EnumerateTree(dir, options))
                yield return (file, key);
        }
    }

    private static IEnumerable<string> EnumerateTree(string dir, EnumerationOptions options)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", options).Where(IsCandidateFile))
            yield return file;
        foreach (var sub in Directory.EnumerateDirectories(dir, "*", options).Where(IsCandidateDirectory))
            foreach (var file in EnumerateTree(sub, options))
                yield return file;
    }

    private static bool IsCandidateDirectory(string dir)
    {
        var name = Path.GetFileName(dir);
        return name.Length > 0 && name[0] is not ('_' or '.');
    }

    private static bool IsCandidateFile(string file)
    {
        var name = Path.GetFileName(file);
        if (name.StartsWith("~$", StringComparison.Ordinal) || name.StartsWith('.'))
            return false;
        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            return false;
        return CardScanController.IsAcceptedImage(contentType: null, name);
    }

    /// <summary>True once the file's size + timestamp have held for <see cref="StableFor"/> and nothing
    /// else has it open. Records/refreshes the sighting otherwise.</summary>
    private bool IsSettled(string file)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(file);
            if (!info.Exists) return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        var now = clock.GetUtcNow();
        if (!_sightings.TryGetValue(file, out var s) || s.Size != info.Length || s.LastWriteUtc != info.LastWriteTimeUtc)
        {
            _sightings[file] = new Sighting(info.Length, info.LastWriteTimeUtc, now);
            return false;
        }
        if (now - s.Since < StableFor || info.Length == 0)
            return false;

        try
        {
            using var _ = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // still held by the scanner software — try again next run
        }
    }

    private enum IngestResult { Failed, Ingested, IngestedMoveFailed }

    private IngestResult Ingest(ScanFolderConfig folder, string file, string folderKey)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        using var ctx = dbFactory.CreateDbContext();

        // Late files join the open batch for this subfolder; once it's committed/discarded, a new one starts.
        var batch = ctx.ScanBatches
            .Where(b => b.Game == folder.Game && b.FolderKey == folderKey
                        && b.Status != ScanBatchStatus.Committed && b.Status != ScanBatchStatus.Discarded)
            .OrderByDescending(b => b.Id)
            .FirstOrDefault();
        var createdBatch = false;
        if (batch is null)
        {
            batch = new ScanBatch
            {
                Game = folder.Game,
                FolderKey = folderKey,
                Name = UniqueName(ctx, folder.Game, folderKey),
                Status = ScanBatchStatus.Collecting,
                CreatedUtc = now,
                LastFileUtc = now,
                IsFoil = folder.IsFoil,
                Condition = folder.Condition,
                Language = folder.Language,
                SetCodes = folder.SetCodes.Count == 0 ? null : string.Join(',', folder.SetCodes),
                DefaultContainerId = folder.DefaultContainerId,
            };
            ctx.ScanBatches.Add(batch);
            ctx.SaveChanges();
            createdBatch = true;
        }

        var sequence = (ctx.ScanBatchItems.Where(i => i.ScanBatchId == batch.Id).Max(i => (int?)i.Sequence) ?? 0) + 1;
        var item = new ScanBatchItem
        {
            ScanBatchId = batch.Id,
            Sequence = sequence,
            OriginalFileName = Truncate(Path.GetFileName(file), 260),
            StoredFileName = "pending",
            Status = ScanBatchItemStatus.Pending,
            State = ScanBatchItemState.Open,
            Condition = string.IsNullOrWhiteSpace(batch.Condition) ? "NM" : batch.Condition,
            Language = batch.Language,
            IsFoil = batch.IsFoil,
        };
        ctx.ScanBatchItems.Add(item);
        ctx.SaveChanges();

        var storedName = item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                         + Path.GetExtension(file).ToLowerInvariant();
        try
        {
            Directory.CreateDirectory(storage.BatchDirectory(batch.Id));
            File.Copy(file, storage.ItemPath(batch.Id, storedName), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Undo the row (and an empty batch we just created); the file is retried next run.
            ctx.ScanBatchItems.Remove(item);
            if (createdBatch) ctx.ScanBatches.Remove(batch);
            ctx.SaveChanges();
            _folderErrors[folder.Game] = ex.Message;
            logger.LogWarning(ex, "Couldn't copy scan {File} into batch storage", file);
            return IngestResult.Failed;
        }

        item.StoredFileName = storedName;
        ctx.SaveChanges();

        // Bump the quiet-period anchor; a Ready batch that gets a late file goes back to Matching.
        // ExecuteUpdate (not a tracked save) so a reviewer's concurrent claim can't trip the row version.
        ctx.ScanBatches.Where(b => b.Id == batch.Id).ExecuteUpdate(u => u
            .SetProperty(b => b.LastFileUtc, now)
            .SetProperty(b => b.ReadyUtc, b => b.Status == ScanBatchStatus.Ready ? null : b.ReadyUtc)
            .SetProperty(b => b.Status, b => b.Status == ScanBatchStatus.Ready ? ScanBatchStatus.Matching : b.Status));

        var destination = ProcessedPath(folder.Path, folderKey, file);
        if (!TryMove(file, destination, out var error))
        {
            _pendingMoves[file] = destination;
            _folderErrors[folder.Game] = $"Couldn't move {Path.GetFileName(file)} to {ProcessedFolderName}: {error}";
            return IngestResult.IngestedMoveFailed;
        }
        return IngestResult.Ingested;
    }

    /// <summary>The folder key, or "key (n)" with the lowest n ≥ 2 not already used by this game.</summary>
    private static string UniqueName(OmniCardDbContext ctx, CardGame game, string folderKey)
    {
        var baseName = Truncate(folderKey, 190);
        var prefix = baseName + " (";
        var taken = ctx.ScanBatches
            .Where(b => b.Game == game && (b.Name == baseName || b.Name.StartsWith(prefix)))
            .Select(b => b.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(baseName))
            return baseName;
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseName} ({n})";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    private static string ProcessedPath(string root, string folderKey, string file) =>
        Path.Combine(root, ProcessedFolderName, folderKey, Path.GetFileName(file));

    private void RetryPendingMoves()
    {
        foreach (var (source, destination) in _pendingMoves.ToList())
        {
            if (!File.Exists(source) || TryMove(source, destination, out _))
                _pendingMoves.Remove(source);
        }
    }

    /// <summary>Move <paramref name="source"/> to <paramref name="destination"/>, adding " (n)" to the
    /// name when something is already there.</summary>
    private bool TryMove(string source, string destination, out string? error)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var target = destination;
            var stem = Path.GetFileNameWithoutExtension(destination);
            var ext = Path.GetExtension(destination);
            for (var n = 2; File.Exists(target); n++)
                target = Path.Combine(Path.GetDirectoryName(destination)!, $"{stem} ({n}){ext}");
            File.Move(source, target);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Couldn't move processed scan {File}", source);
            error = ex.Message;
            return false;
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
