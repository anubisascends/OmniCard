using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Scanning;

namespace OmniCard.Web.Services.ScanBatches;

/// <summary>
/// Saves the Scan page's current scans as a server-side batch, so they can be reviewed later, from
/// another device or by someone else. <see cref="Start"/> creates the batch Uploading and claimed by the
/// uploader (hidden from the list and the nav badge, never matched). Each image then arrives on its own
/// through <see cref="AddItem"/> (one request each keeps every upload under the IIS request limit), with
/// the browser's match and the user's edits, so nothing already matched is matched again.
/// <see cref="Finish"/> releases the batch: Matching and unclaimed, and the background matcher takes any
/// image the browser hadn't matched yet. <see cref="Cancel"/> deletes a half-sent batch; one the browser
/// abandoned is purged after <see cref="AbandonedAfter"/> (<see cref="ScanBatchService.PurgeExpired"/>).
/// Exact copies of images already in an open or committed batch of the game are set aside as duplicates,
/// as in <see cref="ScanFolderIngestor"/> — which also makes a retried upload harmless.
/// </summary>
public sealed class ScanBatchUploadService(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    ScanBatchStorage storage,
    TimeProvider clock,
    ILogger<ScanBatchUploadService> logger)
{
    /// <summary>How long an Uploading batch may go without a new image before it's treated as abandoned.</summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromDays(1);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Create the batch, claimed by the uploader. A blank name becomes "user yyyy-MM-dd HH:mm";
    /// either way " (n)" is added when the game already has a batch by that name.</summary>
    public ScanBatchUploadStartedDto Start(CardGame game, ScanBatchUploadStartRequest request, int userId, string userName)
    {
        using var ctx = dbFactory.CreateDbContext();
        var now = Now;
        var name = string.IsNullOrWhiteSpace(request.Name)
            ? $"{userName} {clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}"
            : request.Name.Trim();
        var setCodes = (request.SetCodes ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var batch = new ScanBatch
        {
            Game = game,
            FolderKey = "",
            Name = ScanFolderIngestor.UniqueName(ctx, game, name),
            Status = ScanBatchStatus.Uploading,
            CreatedUtc = now,
            LastFileUtc = now,
            IsFoil = request.IsFoil,
            Condition = string.IsNullOrWhiteSpace(request.Condition) ? null : request.Condition.Trim(),
            Language = CardLanguages.Normalize(request.Language),
            SetCodes = setCodes.Count == 0 ? null : string.Join(',', setCodes),
            DefaultContainerId = request.DefaultContainerId is > 0 ? request.DefaultContainerId : null,
            ClaimedByUserId = userId,
            ClaimedByName = userName,
            ClaimedUtc = now,
        };
        ctx.ScanBatches.Add(batch);
        ctx.SaveChanges();
        logger.LogInformation("{User} started saving {Game} scan batch {Batch} from the Scan page", userName, game, batch.Name);
        return new ScanBatchUploadStartedDto(batch.Id, batch.Name);
    }

    /// <summary>Store one image of an Uploading batch with its match (null = still to be matched) and
    /// per-copy properties. <paramref name="extension"/> is the stored file's extension (".jpg", …).
    /// Returns whether it was set aside as an exact copy of an image already in a batch.</summary>
    public bool AddItem(int batchId, int userId, byte[] bytes, string fileName, string extension, ScanBatchUploadItem upload)
    {
        using var ctx = dbFactory.CreateDbContext();
        var batch = RequireUploading(ctx, batchId, userId);
        var now = Now;
        var sequence = Math.Max(1, upload.Sequence);
        var originalName = Truncate(string.IsNullOrWhiteSpace(fileName) ? "scan" + extension : Path.GetFileName(fileName), 260);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        if (ScanFolderIngestor.FindOriginal(ctx, batch.Game, hash) is { } original)
        {
            ctx.ScanBatchItems.Add(new ScanBatchItem
            {
                ScanBatchId = batchId,
                Sequence = sequence,
                OriginalFileName = originalName,
                StoredFileName = "",
                ContentHash = hash,
                DuplicateOfItemId = original.Id,
                Status = ScanBatchItemStatus.Pending,
                State = ScanBatchItemState.Duplicate,
            });
            ctx.SaveChanges();
            Touch(ctx, batchId, now);
            return true;
        }

        // The browser's match goes in as is, minus the TIFF data-URI preview (kept as a file instead).
        var match = upload.Match is null ? null : upload.Match with { ScanPreviewDataUri = null };
        var item = new ScanBatchItem
        {
            ScanBatchId = batchId,
            Sequence = sequence,
            OriginalFileName = originalName,
            StoredFileName = "pending",
            ContentHash = hash,
            Status = match is null ? ScanBatchItemStatus.Pending : ScanBatchItemStatus.Matched,
            State = ScanBatchItemState.Open,
            MatchJson = match is null ? null : JsonSerializer.Serialize(match, ScanBatchStorage.Json),
        };
        ScanBatchService.Apply(item, upload.Props);
        if (match is null)
        {
            // The matcher decides these once it has a match (as for a folder batch).
            item.Include = false;
            item.Verified = false;
        }
        ctx.ScanBatchItems.Add(item);
        ctx.SaveChanges();

        var storedName = item.Id.ToString(CultureInfo.InvariantCulture) + extension;
        try
        {
            Directory.CreateDirectory(storage.BatchDirectory(batchId));
            File.WriteAllBytes(storage.ItemPath(batchId, storedName), bytes);
            // Browsers can't show TIFF; the matcher makes this preview too, but a match that came from
            // the browser never goes through it.
            if (ScanBatchStorage.IsTiff(storedName) && WebScanMatchingService.RenderPreviewJpeg(bytes) is { } jpeg)
            {
                var previewName = $"{item.Id}.preview.jpg";
                File.WriteAllBytes(storage.ItemPath(batchId, previewName), jpeg);
                item.PreviewFileName = previewName;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ctx.ScanBatchItems.Remove(item);
            ctx.SaveChanges();
            storage.DeleteFile(batchId, storedName);
            logger.LogWarning(ex, "Couldn't store uploaded scan {File} in batch {Batch}", originalName, batch.Name);
            throw;
        }

        item.StoredFileName = storedName;
        ctx.SaveChanges();
        Touch(ctx, batchId, now);
        return false;
    }

    /// <summary>Release the finished upload: Matching and unclaimed (the matcher marks it Ready once
    /// nothing is pending). A batch of nothing but duplicates closes as Discarded straight away, as a
    /// folder batch does; one that received nothing is deleted.</summary>
    public ScanBatchUploadFinishedDto Finish(int batchId, int userId)
    {
        using var ctx = dbFactory.CreateDbContext();
        var batch = RequireUploading(ctx, batchId, userId);
        var now = Now;
        var open = ctx.ScanBatchItems.Count(i => i.ScanBatchId == batchId && i.State == ScanBatchItemState.Open);
        var duplicates = ctx.ScanBatchItems.Count(i => i.ScanBatchId == batchId && i.State == ScanBatchItemState.Duplicate);
        if (open == 0 && duplicates == 0)
        {
            Delete(ctx, batchId);
            throw new ScanBatchException(ScanBatchErrorKind.BadRequest, "No scans were saved.");
        }

        var status = open > 0 ? ScanBatchStatus.Matching : ScanBatchStatus.Discarded;
        DateTime? closed = status == ScanBatchStatus.Discarded ? now : null;
        ctx.ScanBatches.Where(b => b.Id == batchId && b.Status == ScanBatchStatus.Uploading)
            .ExecuteUpdate(u => u
                .SetProperty(b => b.Status, status)
                .SetProperty(b => b.LastFileUtc, now)
                .SetProperty(b => b.ClosedUtc, closed)
                .SetProperty(b => b.ClaimedByUserId, (int?)null)
                .SetProperty(b => b.ClaimedByName, (string?)null)
                .SetProperty(b => b.ClaimedUtc, (DateTime?)null));
        logger.LogInformation("Saved {Game} scan batch {Batch} from the Scan page: {Count} scan(s), {Duplicates} duplicate(s) set aside",
            batch.Game, batch.Name, open, duplicates);
        return new ScanBatchUploadFinishedDto(batchId, batch.Name, open, duplicates);
    }

    /// <summary>Throw away a batch that's still uploading (the browser gave up). Nothing happens when it
    /// is gone already or was finished.</summary>
    public void Cancel(int batchId, int userId)
    {
        using var ctx = dbFactory.CreateDbContext();
        var batch = ctx.ScanBatches.AsNoTracking().FirstOrDefault(b => b.Id == batchId);
        if (batch is null || batch.Status != ScanBatchStatus.Uploading)
            return;
        if (batch.ClaimedByUserId != userId)
            throw new ScanBatchException(ScanBatchErrorKind.Forbidden, "Only the person saving this batch can cancel it.");
        Delete(ctx, batchId);
        logger.LogInformation("Cancelled saving scan batch {Batch}", batch.Name);
    }

    // --- helpers ---

    private static ScanBatch RequireUploading(OmniCardDbContext ctx, int batchId, int userId)
    {
        var batch = ctx.ScanBatches.AsNoTracking().FirstOrDefault(b => b.Id == batchId)
                    ?? throw new ScanBatchException(ScanBatchErrorKind.NotFound, "Scan batch not found.");
        if (batch.Status != ScanBatchStatus.Uploading)
            throw new ScanBatchException(ScanBatchErrorKind.Conflict, "This batch has already been saved.");
        if (batch.ClaimedByUserId != userId)
            throw new ScanBatchException(ScanBatchErrorKind.Forbidden, "Only the person saving this batch can add scans to it.");
        return batch;
    }

    private static void Touch(OmniCardDbContext ctx, int batchId, DateTime now) =>
        ctx.ScanBatches.Where(b => b.Id == batchId).ExecuteUpdate(u => u.SetProperty(b => b.LastFileUtc, now));

    private void Delete(OmniCardDbContext ctx, int batchId)
    {
        ctx.ScanBatchItems.Where(i => i.ScanBatchId == batchId).ExecuteDelete();
        ctx.ScanBatches.Where(b => b.Id == batchId).ExecuteDelete();
        storage.DeleteBatchDirectory(batchId);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
