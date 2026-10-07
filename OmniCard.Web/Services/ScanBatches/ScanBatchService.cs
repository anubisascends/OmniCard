using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Games;
using OmniCard.Shared.Scanning;

namespace OmniCard.Web.Services.ScanBatches;

public enum ScanBatchErrorKind { NotFound, Conflict, Forbidden, BadRequest }

/// <summary>A batch rule was broken; the controller maps <see cref="Kind"/> to an HTTP status.
/// <see cref="ClaimedBy"/> names the current reviewer on claim conflicts.</summary>
public sealed class ScanBatchException(ScanBatchErrorKind kind, string message, string? claimedBy = null)
    : Exception(message)
{
    public ScanBatchErrorKind Kind { get; } = kind;
    public string? ClaimedBy { get; } = claimedBy;
}

/// <summary>
/// Review-side rules for watched-folder scan batches: listing, the one-reviewer claim, saving the
/// reviewer's edits, commit/remove/rematch/discard, and the retention purge. Every change to a batch's
/// items requires holding its claim. Batch-level state changes use <c>ExecuteUpdate</c> so they never
/// trip the row version against the background matcher; item edits load-patch-save and retry on a
/// concurrency conflict.
/// </summary>
public sealed class ScanBatchService(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    ScanCommitService commits,
    ScanBatchStorage storage,
    TimeProvider clock,
    ILogger<ScanBatchService> logger)
{
    /// <summary>How long a committed/discarded batch stays in the list after it closes.</summary>
    public static readonly TimeSpan ClosedVisibleFor = TimeSpan.FromHours(24);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public IReadOnlyList<ScanBatchSummaryDto> List(int? userId)
    {
        using var ctx = dbFactory.CreateDbContext();
        var closedSince = Now - ClosedVisibleFor;
        var batches = ctx.ScanBatches.AsNoTracking()
            .Where(b => (b.Status != ScanBatchStatus.Committed && b.Status != ScanBatchStatus.Discarded)
                        || b.ClosedUtc >= closedSince)
            .OrderByDescending(b => b.Id)
            .ToList();
        var counts = ItemCounts(ctx, batches.Select(b => b.Id).ToList());
        return batches.Select(b => ToSummary(b, counts, userId)).ToList();
    }

    /// <summary>Open batches nobody has claimed — the Scan nav badge.</summary>
    public int CountUnclaimed()
    {
        using var ctx = dbFactory.CreateDbContext();
        return ctx.ScanBatches.Count(b => b.ClaimedByUserId == null
                                          && b.Status != ScanBatchStatus.Committed
                                          && b.Status != ScanBatchStatus.Discarded);
    }

    public ScanBatchDto Get(int id, int? userId)
    {
        using var ctx = dbFactory.CreateDbContext();
        var batch = ctx.ScanBatches.AsNoTracking().FirstOrDefault(b => b.Id == id) ?? throw NotFound();
        var items = ctx.ScanBatchItems.AsNoTracking()
            .Where(i => i.ScanBatchId == id && i.State == ScanBatchItemState.Open)
            .OrderBy(i => i.Sequence)
            .ToList();
        var counts = ItemCounts(ctx, [id]);
        return new ScanBatchDto
        {
            Summary = ToSummary(batch, counts, userId),
            IsFoil = batch.IsFoil,
            Condition = batch.Condition,
            Language = batch.Language,
            SetCodes = batch.SetCodeList,
            Items = items.Select(ToItemDto).ToList(),
        };
    }

    /// <summary>Take the batch for review. Succeeds when it's unclaimed or already yours; an admin may
    /// <paramref name="force"/> a takeover. Atomic, so two users can't both win.</summary>
    public ScanBatchSummaryDto Claim(int id, int userId, string userName, bool force)
    {
        using var ctx = dbFactory.CreateDbContext();
        var now = Now;
        var updated = ctx.ScanBatches
            .Where(b => b.Id == id
                        && b.Status != ScanBatchStatus.Committed && b.Status != ScanBatchStatus.Discarded
                        && (force || b.ClaimedByUserId == null || b.ClaimedByUserId == userId))
            .ExecuteUpdate(u => u
                .SetProperty(b => b.ClaimedByUserId, userId)
                .SetProperty(b => b.ClaimedByName, userName)
                .SetProperty(b => b.ClaimedUtc, now));
        if (updated == 0)
        {
            var batch = ctx.ScanBatches.AsNoTracking().FirstOrDefault(b => b.Id == id) ?? throw NotFound();
            if (!batch.IsOpen)
                throw new ScanBatchException(ScanBatchErrorKind.Conflict, "This batch is already closed.");
            throw new ScanBatchException(ScanBatchErrorKind.Conflict,
                $"This batch is being reviewed by {batch.ClaimedByName}.", batch.ClaimedByName);
        }
        if (force)
            logger.LogInformation("Scan batch {Id} taken over by {User}", id, userName);
        var claimed = ctx.ScanBatches.AsNoTracking().First(b => b.Id == id);
        return ToSummary(claimed, ItemCounts(ctx, [id]), userId);
    }

    /// <summary>Give up the claim. The reviewer or an admin may release.</summary>
    public void Release(int id, int userId, bool isAdmin)
    {
        using var ctx = dbFactory.CreateDbContext();
        var batch = ctx.ScanBatches.AsNoTracking().FirstOrDefault(b => b.Id == id) ?? throw NotFound();
        if (batch.ClaimedByUserId is null)
            return;
        if (batch.ClaimedByUserId != userId && !isAdmin)
            throw new ScanBatchException(ScanBatchErrorKind.Forbidden, "Only the reviewer or an admin can release this batch.");
        ReleaseClaim(ctx, id);
    }

    /// <summary>Save the reviewer's per-item edits. Match results are never touched here.</summary>
    public void SaveItems(int id, int userId, IReadOnlyList<ScanBatchItemEdit> edits)
    {
        if (edits.Count == 0) return;
        var byId = edits.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.Last());
        for (var attempt = 1; ; attempt++)
        {
            using var ctx = dbFactory.CreateDbContext();
            RequireOwnedOpen(ctx, id, userId);
            var ids = byId.Keys.ToList();
            var items = ctx.ScanBatchItems
                .Where(i => i.ScanBatchId == id && ids.Contains(i.Id) && i.State == ScanBatchItemState.Open)
                .ToList();
            foreach (var item in items)
                Apply(item, byId[item.Id]);
            try
            {
                ctx.SaveChanges();
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                // The background matcher wrote one of these rows meanwhile — reload and re-apply.
            }
        }
    }

    /// <summary>Drop items from the batch and delete their images. Closes the batch when nothing is left.</summary>
    public bool RemoveItems(int id, int userId, IReadOnlyList<int> itemIds)
    {
        using var ctx = dbFactory.CreateDbContext();
        RequireOwnedOpen(ctx, id, userId);
        var ids = itemIds.Distinct().ToList();
        var files = ctx.ScanBatchItems.AsNoTracking()
            .Where(i => i.ScanBatchId == id && ids.Contains(i.Id) && i.State == ScanBatchItemState.Open)
            .Select(i => new { i.StoredFileName, i.PreviewFileName })
            .ToList();
        ctx.ScanBatchItems
            .Where(i => i.ScanBatchId == id && ids.Contains(i.Id) && i.State == ScanBatchItemState.Open)
            .ExecuteUpdate(u => u.SetProperty(i => i.State, ScanBatchItemState.Removed));
        foreach (var f in files)
        {
            storage.DeleteFile(id, f.StoredFileName);
            storage.DeleteFile(id, f.PreviewFileName);
        }
        return CloseIfEmpty(ctx, id);
    }

    /// <summary>Queue items to be matched again (e.g. after an error). A Ready batch goes back to Matching.</summary>
    public void Rematch(int id, int userId, IReadOnlyList<int> itemIds)
    {
        using var ctx = dbFactory.CreateDbContext();
        RequireOwnedOpen(ctx, id, userId);
        var ids = itemIds.Distinct().ToList();
        var count = ctx.ScanBatchItems
            .Where(i => i.ScanBatchId == id && ids.Contains(i.Id) && i.State == ScanBatchItemState.Open
                        && i.Status != ScanBatchItemStatus.Pending)
            .ExecuteUpdate(u => u
                .SetProperty(i => i.Status, ScanBatchItemStatus.Pending)
                .SetProperty(i => i.MatchJson, (string?)null)
                .SetProperty(i => i.Error, (string?)null)
                .SetProperty(i => i.Include, false)
                .SetProperty(i => i.Verified, false));
        if (count > 0)
            ctx.ScanBatches
                .Where(b => b.Id == id && b.Status == ScanBatchStatus.Ready)
                .ExecuteUpdate(u => u
                    .SetProperty(b => b.Status, ScanBatchStatus.Matching)
                    .SetProperty(b => b.ReadyUtc, (DateTime?)null));
    }

    /// <summary>Write the listed items into <paramref name="containerId"/> from the server's saved copy.
    /// The items are marked committed first (so a double submit can't create duplicate lots) and put
    /// back if the write fails.</summary>
    public ScanBatchCommitResultDto Commit(int id, int userId, int containerId, IReadOnlyList<int> itemIds)
    {
        if (containerId <= 0)
            throw new ScanBatchException(ScanBatchErrorKind.BadRequest, "A target location is required.");
        var ids = itemIds.Distinct().ToList();
        if (ids.Count == 0)
            throw new ScanBatchException(ScanBatchErrorKind.BadRequest, "No cards to commit.");

        using var ctx = dbFactory.CreateDbContext();
        var batch = RequireOwnedOpen(ctx, id, userId);
        var items = ctx.ScanBatchItems.AsNoTracking()
            .Where(i => i.ScanBatchId == id && ids.Contains(i.Id) && i.State == ScanBatchItemState.Open)
            .OrderBy(i => i.Sequence)
            .ToList();
        if (items.Count != ids.Count)
            throw new ScanBatchException(ScanBatchErrorKind.Conflict, "Some of these cards were already committed or removed. Reload the batch.");

        var commitItems = new List<ScanCommitItem>(items.Count);
        foreach (var item in items)
            commitItems.Add(ToCommitItem(batch.Game.ToString(), item)
                ?? throw new ScanBatchException(ScanBatchErrorKind.BadRequest, $"'{item.OriginalFileName}' has no matched card yet."));

        var marked = ctx.ScanBatchItems
            .Where(i => i.ScanBatchId == id && ids.Contains(i.Id) && i.State == ScanBatchItemState.Open)
            .ExecuteUpdate(u => u.SetProperty(i => i.State, ScanBatchItemState.Committed));
        if (marked != ids.Count)
        {
            RevertCommitted(ctx, id, ids);
            throw new ScanBatchException(ScanBatchErrorKind.Conflict, "Some of these cards were already committed. Reload the batch.");
        }

        IReadOnlyList<int> lotIds;
        try
        {
            lotIds = commits.Commit(containerId, commitItems);
        }
        catch
        {
            RevertCommitted(ctx, id, ids);
            throw;
        }

        var closed = CloseIfEmpty(ctx, id);
        logger.LogInformation("Committed {Count} card(s) from scan batch {Batch} to location {LocationId}",
            lotIds.Count, batch.Name, containerId);
        return new ScanBatchCommitResultDto(lotIds.Count, closed);
    }

    /// <summary>Throw away every open item and close the batch. The reviewer or an admin may discard;
    /// an unclaimed batch can be discarded by anyone allowed to commit scans.</summary>
    public void Discard(int id, int userId, bool isAdmin)
    {
        using var ctx = dbFactory.CreateDbContext();
        var batch = ctx.ScanBatches.AsNoTracking().FirstOrDefault(b => b.Id == id) ?? throw NotFound();
        if (!batch.IsOpen)
            return;
        if (batch.ClaimedByUserId is { } owner && owner != userId && !isAdmin)
            throw new ScanBatchException(ScanBatchErrorKind.Conflict,
                $"This batch is being reviewed by {batch.ClaimedByName}.", batch.ClaimedByName);

        var files = ctx.ScanBatchItems.AsNoTracking()
            .Where(i => i.ScanBatchId == id && i.State == ScanBatchItemState.Open)
            .Select(i => new { i.StoredFileName, i.PreviewFileName })
            .ToList();
        ctx.ScanBatchItems
            .Where(i => i.ScanBatchId == id && i.State == ScanBatchItemState.Open)
            .ExecuteUpdate(u => u.SetProperty(i => i.State, ScanBatchItemState.Removed));
        foreach (var f in files)
        {
            storage.DeleteFile(id, f.StoredFileName);
            storage.DeleteFile(id, f.PreviewFileName);
        }
        Close(ctx, id, ScanBatchStatus.Discarded);
        logger.LogInformation("Discarded scan batch {Batch}", batch.Name);
    }

    /// <summary>Delete closed batches (rows + stored images) older than <paramref name="retentionDays"/>.</summary>
    public int PurgeExpired(int retentionDays)
    {
        using var ctx = dbFactory.CreateDbContext();
        var cutoff = Now - TimeSpan.FromDays(retentionDays);
        var expired = ctx.ScanBatches
            .Where(b => (b.Status == ScanBatchStatus.Committed || b.Status == ScanBatchStatus.Discarded)
                        && b.ClosedUtc < cutoff)
            .Select(b => b.Id)
            .ToList();
        if (expired.Count == 0)
            return 0;
        foreach (var id in expired)
            storage.DeleteBatchDirectory(id);
        ctx.ScanBatchItems.Where(i => expired.Contains(i.ScanBatchId)).ExecuteDelete();
        ctx.ScanBatches.Where(b => expired.Contains(b.Id)).ExecuteDelete();
        logger.LogInformation("Purged {Count} expired scan batch(es)", expired.Count);
        return expired.Count;
    }

    /// <summary>The file to serve for an item's image: its JPEG preview for TIFFs, else the scan itself.</summary>
    public (string Path, string ContentType)? ImageFile(int batchId, int itemId)
    {
        using var ctx = dbFactory.CreateDbContext();
        var item = ctx.ScanBatchItems.AsNoTracking().FirstOrDefault(i => i.Id == itemId && i.ScanBatchId == batchId);
        if (item is null) return null;
        var name = item.PreviewFileName ?? item.StoredFileName;
        var path = storage.ItemPath(batchId, name);
        if (!File.Exists(path)) return null;
        var contentType = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".tif" or ".tiff" => "image/tiff",
            _ => "image/jpeg",
        };
        return (path, contentType);
    }

    // --- helpers ---

    private static ScanBatchException NotFound() => new(ScanBatchErrorKind.NotFound, "Scan batch not found.");

    /// <summary>Load an open batch the caller holds the claim on, or throw.</summary>
    private static ScanBatch RequireOwnedOpen(OmniCardDbContext ctx, int id, int userId)
    {
        var batch = ctx.ScanBatches.AsNoTracking().FirstOrDefault(b => b.Id == id) ?? throw NotFound();
        if (!batch.IsOpen)
            throw new ScanBatchException(ScanBatchErrorKind.Conflict, "This batch is already closed.");
        if (batch.ClaimedByUserId != userId)
            throw new ScanBatchException(ScanBatchErrorKind.Conflict,
                batch.ClaimedByName is null
                    ? "Open this batch for review before changing it."
                    : $"This batch is being reviewed by {batch.ClaimedByName}.",
                batch.ClaimedByName);
        return batch;
    }

    private static void ReleaseClaim(OmniCardDbContext ctx, int id) =>
        ctx.ScanBatches.Where(b => b.Id == id).ExecuteUpdate(u => u
            .SetProperty(b => b.ClaimedByUserId, (int?)null)
            .SetProperty(b => b.ClaimedByName, (string?)null)
            .SetProperty(b => b.ClaimedUtc, (DateTime?)null));

    private void RevertCommitted(OmniCardDbContext ctx, int id, List<int> ids) =>
        ctx.ScanBatchItems
            .Where(i => i.ScanBatchId == id && ids.Contains(i.Id) && i.State == ScanBatchItemState.Committed)
            .ExecuteUpdate(u => u.SetProperty(i => i.State, ScanBatchItemState.Open));

    /// <summary>Close the batch once no open items remain: Committed if anything was committed, else
    /// Discarded. Returns whether it closed.</summary>
    private bool CloseIfEmpty(OmniCardDbContext ctx, int id)
    {
        if (ctx.ScanBatchItems.Any(i => i.ScanBatchId == id && i.State == ScanBatchItemState.Open))
            return false;
        var anyCommitted = ctx.ScanBatchItems.Any(i => i.ScanBatchId == id && i.State == ScanBatchItemState.Committed);
        Close(ctx, id, anyCommitted ? ScanBatchStatus.Committed : ScanBatchStatus.Discarded);
        return true;
    }

    private void Close(OmniCardDbContext ctx, int id, ScanBatchStatus status)
    {
        var now = Now;
        ctx.ScanBatches.Where(b => b.Id == id).ExecuteUpdate(u => u
            .SetProperty(b => b.Status, status)
            .SetProperty(b => b.ClosedUtc, now)
            .SetProperty(b => b.ClaimedByUserId, (int?)null)
            .SetProperty(b => b.ClaimedByName, (string?)null)
            .SetProperty(b => b.ClaimedUtc, (DateTime?)null));
    }

    private sealed record Counts(int Total, int Pending, int Matched, int Errors, int Committed);

    private static Dictionary<int, Counts> ItemCounts(OmniCardDbContext ctx, List<int> batchIds)
    {
        var rows = ctx.ScanBatchItems.AsNoTracking()
            .Where(i => batchIds.Contains(i.ScanBatchId))
            .GroupBy(i => new { i.ScanBatchId, i.State, i.Status })
            .Select(g => new { g.Key.ScanBatchId, g.Key.State, g.Key.Status, Count = g.Count() })
            .ToList();
        return rows.GroupBy(r => r.ScanBatchId).ToDictionary(g => g.Key, g =>
        {
            var open = g.Where(r => r.State == ScanBatchItemState.Open).ToList();
            return new Counts(
                open.Sum(r => r.Count),
                open.Where(r => r.Status == ScanBatchItemStatus.Pending).Sum(r => r.Count),
                open.Where(r => r.Status == ScanBatchItemStatus.Matched).Sum(r => r.Count),
                open.Where(r => r.Status == ScanBatchItemStatus.Error).Sum(r => r.Count),
                g.Where(r => r.State == ScanBatchItemState.Committed).Sum(r => r.Count));
        });
    }

    private static ScanBatchSummaryDto ToSummary(ScanBatch b, Dictionary<int, Counts> counts, int? userId)
    {
        var c = counts.GetValueOrDefault(b.Id) ?? new Counts(0, 0, 0, 0, 0);
        return new ScanBatchSummaryDto
        {
            Id = b.Id,
            Name = b.Name,
            Game = b.Game.ToString(),
            Status = b.Status.ToString(),
            Total = c.Total,
            Pending = c.Pending,
            Matched = c.Matched,
            Errors = c.Errors,
            Committed = c.Committed,
            ClaimedBy = b.ClaimedByName,
            ClaimedByMe = userId is not null && b.ClaimedByUserId == userId,
            CreatedUtc = DateTime.SpecifyKind(b.CreatedUtc, DateTimeKind.Utc),
            LastFileUtc = DateTime.SpecifyKind(b.LastFileUtc, DateTimeKind.Utc),
            ReadyUtc = b.ReadyUtc is { } r ? DateTime.SpecifyKind(r, DateTimeKind.Utc) : null,
            ClosedUtc = b.ClosedUtc is { } cl ? DateTime.SpecifyKind(cl, DateTimeKind.Utc) : null,
            DefaultContainerId = b.DefaultContainerId,
        };
    }

    private static ScanBatchItemDto ToItemDto(ScanBatchItem i) => new()
    {
        Id = i.Id,
        Sequence = i.Sequence,
        FileName = i.OriginalFileName,
        ImageUrl = $"/api/scan/batches/{i.ScanBatchId}/items/{i.Id}/image",
        Status = i.Status.ToString(),
        Match = Deserialize<ScanMatchDto>(i.MatchJson),
        Override = Deserialize<ScanSearchResultDto>(i.OverrideJson),
        Error = i.Error,
        Include = i.Include,
        Verified = i.Verified,
        Condition = i.Condition,
        Language = i.Language,
        IsFoil = i.IsFoil,
        FoilType = i.FoilType,
        Quantity = i.Quantity,
        PurchasePrice = i.PurchasePrice,
        Tags = Deserialize<List<string>>(i.TagsJson) ?? [],
        Note = i.Note,
    };

    private static void Apply(ScanBatchItem item, ScanBatchItemEdit e)
    {
        item.Include = e.Include;
        item.Verified = e.Verified;
        item.OverrideJson = e.Override is null ? null : JsonSerializer.Serialize(e.Override, ScanBatchStorage.Json);
        item.Condition = string.IsNullOrWhiteSpace(e.Condition) ? "NM" : e.Condition.Trim();
        item.Language = CardLanguages.Normalize(e.Language);
        item.IsFoil = e.IsFoil;
        item.FoilType = e.IsFoil && !string.IsNullOrWhiteSpace(e.FoilType) ? e.FoilType.Trim() : null;
        item.Quantity = Math.Clamp(e.Quantity, 1, 9999);
        item.PurchasePrice = e.PurchasePrice is < 0 ? null : e.PurchasePrice;
        var tags = (e.Tags ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct().ToList();
        item.TagsJson = tags.Count == 0 ? null : JsonSerializer.Serialize(tags, ScanBatchStorage.Json);
        item.Note = string.IsNullOrWhiteSpace(e.Note) ? null : e.Note.Trim();
    }

    /// <summary>The commit payload for one item — the user's correction wins over the auto-match (same
    /// rule as the SPA's identityOf). Null when the item has no identity.</summary>
    internal static ScanCommitItem? ToCommitItem(string game, ScanBatchItem item)
    {
        var match = Deserialize<ScanMatchDto>(item.MatchJson);
        var correction = Deserialize<ScanSearchResultDto>(item.OverrideJson);
        ScanCommitItem baseItem;
        if (correction is not null)
            baseItem = new ScanCommitItem
            {
                GameCardId = correction.GameCardId, Name = correction.Name, SetCode = correction.SetCode,
                SetName = correction.SetName, CollectorNumber = correction.CollectorNumber,
                Rarity = correction.Rarity, ImageUri = correction.ImageUri,
            };
        else if (match is { Matched: true })
            baseItem = new ScanCommitItem
            {
                GameCardId = match.GameCardId ?? "", Name = match.Name ?? "", SetCode = match.SetCode ?? "",
                SetName = match.SetName ?? "", CollectorNumber = match.CollectorNumber ?? "",
                Rarity = match.Rarity ?? "", ImageUri = match.ImageUri,
            };
        else
            return null;

        return baseItem with
        {
            Game = game,
            Condition = item.Condition,
            Language = item.Language,
            IsFoil = item.IsFoil,
            FoilType = item.IsFoil ? item.FoilType : null,
            Quantity = item.Quantity,
            PurchasePrice = item.PurchasePrice,
            Note = item.Note,
            Tags = Deserialize<List<string>>(item.TagsJson) ?? [],
            ScanHash = string.IsNullOrEmpty(match?.ScanHash) ? null : match.ScanHash,
        };
    }

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, ScanBatchStorage.Json); }
        catch (JsonException) { return null; }
    }
}
