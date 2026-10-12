using System.Collections.Concurrent;
using System.Security.Cryptography;
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
/// Every file is SHA-256 hashed on the way in. A scanner can't produce the same bytes twice for two
/// physical passes (sensor noise alone differs), so a file whose hash matches an open or committed item
/// of the same game is a re-save of an image already taken — capture software that wrote the whole run
/// out a second time. Those are recorded as <see cref="ScanBatchItemState.Duplicate"/> rows (no stored
/// image, never matched) and their originals still go to <c>_processed</c>.
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

    /// <summary>Scan every enabled folder once; returns the number of files taken in (duplicates set
    /// aside included).</summary>
    public int RunOnce(ScanFolderSettings settings)
    {
        RetryPendingMoves();

        var ingested = 0;
        var duplicates = 0;
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
                        case IngestResult.Duplicate: ingested++; duplicates++; break;
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
        if (duplicates > 0)
            logger.LogWarning("Set aside {Count} scan image(s) that were exact copies of images already picked up", duplicates);
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

    private enum IngestResult { Failed, Ingested, Duplicate, IngestedMoveFailed }

    /// <summary>SHA-256 of a file's bytes as lowercase hex.</summary>
    internal static string ContentHash(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>The first open or committed item of <paramref name="game"/> holding the same bytes, or
    /// null. Removed and discarded items don't count, so re-dropping images whose batch was thrown
    /// away picks them up again.</summary>
    internal static ScanBatchItem? FindOriginal(OmniCardDbContext ctx, CardGame game, string hash) =>
        (from i in ctx.ScanBatchItems.AsNoTracking()
         join b in ctx.ScanBatches.AsNoTracking() on i.ScanBatchId equals b.Id
         where i.ContentHash == hash && b.Game == game
               && (i.State == ScanBatchItemState.Open || i.State == ScanBatchItemState.Committed)
         orderby i.Id
         select i).FirstOrDefault();

    private IngestResult Ingest(ScanFolderConfig folder, string file, string folderKey)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        string hash;
        try
        {
            hash = ContentHash(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Couldn't read scan {File}", file);
            return IngestResult.Failed; // retried next run
        }

        using var ctx = dbFactory.CreateDbContext();
        var original = FindOriginal(ctx, folder.Game, hash);

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
        if (original is not null)
            return SetAsideDuplicate(ctx, folder, file, folderKey, batch.Id, sequence, hash, original, now);

        var item = new ScanBatchItem
        {
            ScanBatchId = batch.Id,
            Sequence = sequence,
            OriginalFileName = Truncate(Path.GetFileName(file), 260),
            StoredFileName = "pending",
            ContentHash = hash,
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

        return MoveToProcessed(folder, file, folderKey, IngestResult.Ingested);
    }

    /// <summary>Record an exact copy of <paramref name="original"/> without storing or matching it. It
    /// keeps the batch's quiet period alive (the copies usually stream in as a run) but never sends a
    /// Ready batch back to matching.</summary>
    private IngestResult SetAsideDuplicate(OmniCardDbContext ctx, ScanFolderConfig folder, string file, string folderKey,
        int batchId, int sequence, string hash, ScanBatchItem original, DateTime now)
    {
        ctx.ScanBatchItems.Add(new ScanBatchItem
        {
            ScanBatchId = batchId,
            Sequence = sequence,
            OriginalFileName = Truncate(Path.GetFileName(file), 260),
            StoredFileName = "",
            ContentHash = hash,
            DuplicateOfItemId = original.Id,
            Status = ScanBatchItemStatus.Pending,
            State = ScanBatchItemState.Duplicate,
        });
        ctx.SaveChanges();
        ctx.ScanBatches.Where(b => b.Id == batchId).ExecuteUpdate(u => u.SetProperty(b => b.LastFileUtc, now));
        logger.LogDebug("Scan {File} is an exact copy of batch item {Original}; set aside", file, original.Id);
        return MoveToProcessed(folder, file, folderKey, IngestResult.Duplicate);
    }

    private IngestResult MoveToProcessed(ScanFolderConfig folder, string file, string folderKey, IngestResult success)
    {
        var destination = ProcessedPath(folder.Path, folderKey, file);
        if (!TryMove(file, destination, out var error))
        {
            _pendingMoves[file] = destination;
            _folderErrors[folder.Game] = $"Couldn't move {Path.GetFileName(file)} to {ProcessedFolderName}: {error}";
            return IngestResult.IngestedMoveFailed;
        }
        return success;
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
