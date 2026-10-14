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
/// Watches the scan folders (one per game) and turns what's in them into batches. Each top-level
/// subfolder is a batch; files in a game folder's root form one more. A batch's files are left alone
/// while it waits (<see cref="ScanBatchStatus.Collecting"/>): only their names, sizes and timestamps are
/// read, so the scanner software can keep writing, renaming and finishing its run. Any change restarts
/// that batch's quiet period. The batch starts when someone presses Run (Starting) or, with
/// auto-processing on, once its files have gone the quiet period unchanged. Only then are its files
/// copied into the data directory and the originals moved to
/// <c>&lt;game folder&gt;\_processed\&lt;batch&gt;\</c>. Files that land in the folder after that (or were still
/// locked at the time) form a new batch.
///
/// Every file is SHA-256 hashed when it's taken in. A scanner can't produce the same bytes twice for two
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

    /// <summary>The <see cref="ScanBatch.FolderKey"/> of a batch of files dropped in the game folder's root.</summary>
    internal const string RootKey = "";

    private readonly record struct PendingMove(string Destination, int ItemId);

    // The last-seen file list of each waiting batch, to notice changes. Not persisted: after a restart
    // every waiting batch's quiet period starts over, which errs on the side of leaving files alone.
    private readonly Dictionary<(CardGame Game, string Key), int> _fingerprints = new(FingerprintKeyComparer.Instance);

    // Files copied into a batch whose move to _processed failed: source path → destination. Retried
    // each run, and skipped by enumeration so they aren't taken in twice.
    private readonly Dictionary<string, PendingMove> _pendingMoves = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<CardGame, string> _folderErrors = new();

    /// <summary>True while a batch is waiting to start or a move is pending — the caller should run
    /// again on its next pass rather than wait for the periodic rescan.</summary>
    public bool HasWaitingBatches { get; private set; }

    /// <summary>The last problem hit in each game's folder (missing, unreadable, move failed), for the
    /// settings page. Cleared once a run of that folder succeeds.</summary>
    public IReadOnlyDictionary<CardGame, string> FolderErrors => _folderErrors;

    /// <summary>Scan every enabled folder once: note new and changed batches, and take in the files of
    /// batches that are due to start. Returns the number of files taken in (duplicates set aside
    /// included).</summary>
    public int RunOnce(ScanFolderSettings settings)
    {
        RetryPendingMoves();

        var taken = 0;
        var waiting = false;
        var seen = new HashSet<(CardGame, string)>(FingerprintKeyComparer.Instance);
        foreach (var folder in settings.Folders.Where(f => f.Enabled))
        {
            try
            {
                if (!Directory.Exists(folder.Path))
                {
                    _folderErrors[folder.Game] = $"Folder not found: {folder.Path}";
                    continue;
                }
                var (folderTaken, folderWaiting, error) = RunFolder(folder, settings, seen);
                taken += folderTaken;
                waiting |= folderWaiting;
                if (error is null)
                    _folderErrors.TryRemove(folder.Game, out _);
                else
                    _folderErrors[folder.Game] = error;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _folderErrors[folder.Game] = ex.Message;
                logger.LogWarning(ex, "Couldn't read the {Game} scan folder {Path}", folder.Game, folder.Path);
            }
        }

        // Forget batches that started or were dropped.
        foreach (var gone in _fingerprints.Keys.Where(k => !seen.Contains(k)).ToList())
            _fingerprints.Remove(gone);

        HasWaitingBatches = waiting || _pendingMoves.Count > 0;
        if (taken > 0)
            logger.LogInformation("Took in {Count} scan image(s) from watched folders", taken);
        return taken;
    }

    private (int Taken, bool Waiting, string? Error) RunFolder(
        ScanFolderConfig folder, ScanFolderSettings settings, HashSet<(CardGame, string)> seen)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var groups = EnumerateCandidates(folder.Path)
            .Where(c => !_pendingMoves.ContainsKey(c.File.FullName))
            .GroupBy(c => c.FolderKey ?? RootKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(c => c.File).OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        using var ctx = dbFactory.CreateDbContext();
        var batches = ctx.ScanBatches.AsNoTracking()
            .Where(b => b.Game == folder.Game && (b.Status == ScanBatchStatus.Collecting || b.Status == ScanBatchStatus.Starting))
            .OrderBy(b => b.Id)
            .ToList();

        // Note what's in the folder: new batches, and changes that restart a batch's quiet period.
        foreach (var (key, files) in groups)
        {
            var id = (folder.Game, key);
            seen.Add(id);
            var fingerprint = Fingerprint(files);
            var batch = batches.FirstOrDefault(b => string.Equals(b.FolderKey, key, StringComparison.OrdinalIgnoreCase));
            if (batch is null)
            {
                batch = NewBatch(ctx, folder, key, files.Count, now);
                batches.Add(batch);
                logger.LogInformation("New {Game} scan batch {Batch} is waiting ({Count} file(s))", folder.Game, batch.Name, files.Count);
            }
            else if (!_fingerprints.TryGetValue(id, out var last) || last != fingerprint)
            {
                ctx.ScanBatches.Where(b => b.Id == batch.Id).ExecuteUpdate(u => u
                    .SetProperty(b => b.LastFileUtc, now)
                    .SetProperty(b => b.WaitingFiles, files.Count));
                batch.LastFileUtc = now;
                batch.WaitingFiles = files.Count;
            }
            _fingerprints[id] = fingerprint;
        }

        var quietCutoff = now - TimeSpan.FromSeconds(settings.QuietPeriodSeconds);
        var taken = 0;
        var waiting = false;
        string? error = null;
        foreach (var batch in batches)
        {
            var files = groups.GetValueOrDefault(batch.FolderKey) ?? [];
            if (files.Count == 0 && !ctx.ScanBatchItems.Any(i => i.ScanBatchId == batch.Id))
            {
                // Its files were deleted or moved away before it started: nothing left to wait for.
                ctx.ScanBatches.Where(b => b.Id == batch.Id
                                           && (b.Status == ScanBatchStatus.Collecting || b.Status == ScanBatchStatus.Starting))
                    .ExecuteDelete();
                continue;
            }

            var due = batch.Status == ScanBatchStatus.Starting
                      || (settings.AutoProcess && batch.LastFileUtc <= quietCutoff);
            if (!due)
            {
                waiting = true;
                continue;
            }

            var result = TakeIn(folder, settings, batch, files);
            taken += result.Taken;
            error ??= result.Error;
            _fingerprints.Remove((folder.Game, batch.FolderKey));
            seen.Remove((folder.Game, batch.FolderKey));
            waiting |= result.StillWaiting;
        }
        return (taken, waiting, error);
    }

    private ScanBatch NewBatch(OmniCardDbContext ctx, ScanFolderConfig folder, string key, int fileCount, DateTime now)
    {
        var batch = new ScanBatch
        {
            Game = folder.Game,
            FolderKey = key,
            Name = UniqueName(ctx, folder.Game, key == RootKey ? DateKey() : key),
            Status = ScanBatchStatus.Collecting,
            CreatedUtc = now,
            LastFileUtc = now,
            WaitingFiles = fileCount,
            IsFoil = folder.IsFoil,
            Condition = folder.Condition,
            Language = folder.Language,
            SetCodes = folder.SetCodes.Count == 0 ? null : string.Join(',', folder.SetCodes),
            DefaultContainerId = folder.DefaultContainerId,
        };
        ctx.ScanBatches.Add(batch);
        ctx.SaveChanges();
        ctx.Entry(batch).State = EntityState.Detached;
        return batch;
    }

    private readonly record struct TakeInResult(int Taken, bool StillWaiting, string? Error);

    /// <summary>Copy a starting batch's files in, move the originals to <c>_processed</c> and hand the
    /// batch to the matcher. A file that's empty or still held open by another program is left where it
    /// is (it'll form the next batch). A batch that ends up holding only duplicates is closed straight
    /// away; one that got nothing at all goes back to waiting.</summary>
    private TakeInResult TakeIn(ScanFolderConfig folder, ScanFolderSettings settings, ScanBatch batch, List<FileInfo> files)
    {
        var taken = 0;
        var duplicates = 0;
        string? error = null;
        using var ctx = dbFactory.CreateDbContext();
        var sequence = ctx.ScanBatchItems.Where(i => i.ScanBatchId == batch.Id).Max(i => (int?)i.Sequence) ?? 0;
        foreach (var file in files)
        {
            if (!IsReadyToTake(file))
                continue;
            var result = Ingest(ctx, folder, batch, file.FullName, sequence + 1);
            ctx.ChangeTracker.Clear();
            switch (result.Outcome)
            {
                case IngestOutcome.Ingested: taken++; sequence++; break;
                case IngestOutcome.Duplicate: taken++; duplicates++; sequence++; break;
            }
            error ??= result.Error;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var anyOpen = ctx.ScanBatchItems.Any(i => i.ScanBatchId == batch.Id
                                                   && (i.State == ScanBatchItemState.Open || i.State == ScanBatchItemState.Committed));
        var anyDuplicate = ctx.ScanBatchItems.Any(i => i.ScanBatchId == batch.Id && i.State == ScanBatchItemState.Duplicate);
        var waitingQuery = ctx.ScanBatches.Where(b => b.Id == batch.Id
                                                      && (b.Status == ScanBatchStatus.Collecting || b.Status == ScanBatchStatus.Starting));
        if (anyOpen)
        {
            waitingQuery.ExecuteUpdate(u => u
                .SetProperty(b => b.Status, ScanBatchStatus.Matching)
                .SetProperty(b => b.WaitingFiles, 0));
            logger.LogInformation("Started {Game} scan batch {Batch}: took in {Count} file(s)", folder.Game, batch.Name, taken);
        }
        else if (anyDuplicate)
        {
            // Nothing in it but copies of earlier scans: nothing to review. It stays listed for a day
            // (closed), so the skipped copies are still reported.
            waitingQuery.ExecuteUpdate(u => u
                .SetProperty(b => b.Status, ScanBatchStatus.Discarded)
                .SetProperty(b => b.ClosedUtc, now)
                .SetProperty(b => b.WaitingFiles, 0));
            if (settings.DeleteOriginalsOnClose)
                ScanBatchStorage.DeleteOriginals(
                    ctx.ScanBatchItems.Where(i => i.ScanBatchId == batch.Id).Select(i => i.ProcessedPath).ToList(), logger);
        }
        else
        {
            // Every file was still in use (or failed): wait a full quiet period and try again.
            waitingQuery.ExecuteUpdate(u => u
                .SetProperty(b => b.Status, ScanBatchStatus.Collecting)
                .SetProperty(b => b.LastFileUtc, now));
            return new TakeInResult(0, StillWaiting: true, error);
        }

        if (duplicates > 0)
            logger.LogWarning("Set aside {Count} scan image(s) in {Batch} that were exact copies of images already picked up",
                duplicates, batch.Name);
        return new TakeInResult(taken, StillWaiting: false, error);
    }

    private string DateKey() => clock.GetLocalNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A hash of the batch's file list — names, sizes and timestamps — so a rename, a new file
    /// or a file still being written all count as a change.</summary>
    private static int Fingerprint(List<FileInfo> files)
    {
        var hash = new HashCode();
        foreach (var f in files)
        {
            hash.Add(f.FullName, StringComparer.OrdinalIgnoreCase);
            hash.Add(f.Length);
            hash.Add(f.LastWriteTimeUtc.Ticks);
        }
        return hash.ToHashCode();
    }

    /// <summary>Image files under <paramref name="root"/>, each with its batch key: the top-level
    /// subfolder name, or null for files in the root. Skips <c>_*</c> and <c>.*</c> folders (which
    /// covers <c>_processed</c>) and hidden/system/temporary files. Reads directory entries only — no
    /// file is opened.</summary>
    internal static IEnumerable<(FileInfo File, string? FolderKey)> EnumerateCandidates(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        var rootDir = new DirectoryInfo(root);
        foreach (var file in rootDir.EnumerateFiles("*", options).Where(IsCandidateFile))
            yield return (file, null);

        foreach (var dir in rootDir.EnumerateDirectories("*", options).Where(IsCandidateDirectory))
            foreach (var file in EnumerateTree(dir, options))
                yield return (file, dir.Name);
    }

    private static IEnumerable<FileInfo> EnumerateTree(DirectoryInfo dir, EnumerationOptions options)
    {
        foreach (var file in dir.EnumerateFiles("*", options).Where(IsCandidateFile))
            yield return file;
        foreach (var sub in dir.EnumerateDirectories("*", options).Where(IsCandidateDirectory))
            foreach (var file in EnumerateTree(sub, options))
                yield return file;
    }

    private static bool IsCandidateDirectory(DirectoryInfo dir) =>
        dir.Name.Length > 0 && dir.Name[0] is not ('_' or '.');

    private static bool IsCandidateFile(FileInfo file)
    {
        var name = file.Name;
        if (name.StartsWith("~$", StringComparison.Ordinal) || name.StartsWith('.'))
            return false;
        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            return false;
        return CardScanController.IsAcceptedImage(contentType: null, name);
    }

    /// <summary>True when the file has content and nothing else has it open — checked only once the
    /// batch is starting, never while it waits.</summary>
    private static bool IsReadyToTake(FileInfo file)
    {
        try
        {
            file.Refresh();
            if (!file.Exists || file.Length == 0)
                return false;
            using var _ = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // still held by the scanner software — left for the next batch
        }
    }

    private enum IngestOutcome { Failed, Ingested, Duplicate }

    private readonly record struct IngestResult(IngestOutcome Outcome, string? Error = null);

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

    private IngestResult Ingest(OmniCardDbContext ctx, ScanFolderConfig folder, ScanBatch batch, string file, int sequence)
    {
        string hash;
        try
        {
            hash = ContentHash(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Couldn't read scan {File}", file);
            return new IngestResult(IngestOutcome.Failed); // left in place for the next batch
        }

        var original = FindOriginal(ctx, folder.Game, hash);
        if (original is not null)
        {
            var duplicate = new ScanBatchItem
            {
                ScanBatchId = batch.Id,
                Sequence = sequence,
                OriginalFileName = Truncate(Path.GetFileName(file), 260),
                StoredFileName = "",
                ContentHash = hash,
                DuplicateOfItemId = original.Id,
                Status = ScanBatchItemStatus.Pending,
                State = ScanBatchItemState.Duplicate,
            };
            ctx.ScanBatchItems.Add(duplicate);
            ctx.SaveChanges();
            logger.LogDebug("Scan {File} is an exact copy of batch item {Original}; set aside", file, original.Id);
            return new IngestResult(IngestOutcome.Duplicate, MoveToProcessed(ctx, folder, batch, file, duplicate));
        }

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
            // Undo the row; the file stays in the folder and is picked up with the next batch.
            ctx.ScanBatchItems.Remove(item);
            ctx.SaveChanges();
            logger.LogWarning(ex, "Couldn't copy scan {File} into batch storage", file);
            return new IngestResult(IngestOutcome.Failed, ex.Message);
        }

        item.StoredFileName = storedName;
        ctx.SaveChanges();
        return new IngestResult(IngestOutcome.Ingested, MoveToProcessed(ctx, folder, batch, file, item));
    }

    /// <summary>Move a taken-in file to <c>_processed</c> and record where it went. Returns an error
    /// message when the move failed (it's retried on later runs).</summary>
    private string? MoveToProcessed(OmniCardDbContext ctx, ScanFolderConfig folder, ScanBatch batch, string file, ScanBatchItem item)
    {
        var destination = ProcessedPath(folder.Path, batch, file);
        if (!TryMove(file, destination, out var moved, out var error))
        {
            _pendingMoves[file] = new PendingMove(destination, item.Id);
            return $"Couldn't move {Path.GetFileName(file)} to {ProcessedFolderName}: {error}";
        }
        item.ProcessedPath = Truncate(moved!, 1024);
        ctx.SaveChanges();
        return null;
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

    /// <summary><c>_processed\&lt;subfolder&gt;\&lt;file&gt;</c>; root files go under the batch's (date) name.</summary>
    private static string ProcessedPath(string root, ScanBatch batch, string file) =>
        Path.Combine(root, ProcessedFolderName, batch.FolderKey == RootKey ? batch.Name : batch.FolderKey, Path.GetFileName(file));

    private void RetryPendingMoves()
    {
        if (_pendingMoves.Count == 0)
            return;
        using var ctx = dbFactory.CreateDbContext();
        foreach (var (source, pending) in _pendingMoves.ToList())
        {
            if (!File.Exists(source))
            {
                _pendingMoves.Remove(source);
                continue;
            }
            if (!TryMove(source, pending.Destination, out var moved, out _))
                continue;
            _pendingMoves.Remove(source);
            var path = Truncate(moved!, 1024);
            ctx.ScanBatchItems.Where(i => i.Id == pending.ItemId)
                .ExecuteUpdate(u => u.SetProperty(i => i.ProcessedPath, path));
        }
    }

    /// <summary>Move <paramref name="source"/> to <paramref name="destination"/>, adding " (n)" to the
    /// name when something is already there. <paramref name="moved"/> is where it ended up.</summary>
    private bool TryMove(string source, string destination, out string? moved, out string? error)
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
            moved = target;
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Couldn't move processed scan {File}", source);
            moved = null;
            error = ex.Message;
            return false;
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed class FingerprintKeyComparer : IEqualityComparer<(CardGame Game, string Key)>
    {
        public static readonly FingerprintKeyComparer Instance = new();
        public bool Equals((CardGame Game, string Key) x, (CardGame Game, string Key) y) =>
            x.Game == y.Game && string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((CardGame Game, string Key) obj) =>
            HashCode.Combine(obj.Game, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key));
    }
}
