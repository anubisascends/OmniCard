using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Scanning;

namespace OmniCard.Web.Services.ScanBatches;

/// <summary>
/// Matches batch images in the background, one at a time. A Collecting batch starts once its quiet
/// period passes with no new file; items are matched oldest batch first, in ingest order; a batch with
/// nothing left pending becomes Ready. Nothing is held "in flight", so after a restart any Pending item
/// is simply matched again.
/// </summary>
public sealed class ScanBatchProcessor(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    IScanMatcher matcher,
    Func<Shared.Cards.CardGame, string, bool> isNewCard,
    ScanBatchStorage storage,
    TimeProvider clock,
    ILogger<ScanBatchProcessor> logger)
{
    /// <summary>Advance batch states and match the next pending item. Returns false when there was
    /// nothing to match.</summary>
    public async Task<bool> ProcessNextAsync(TimeSpan quietPeriod, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);

        // Collecting → Matching once no file has arrived for the quiet period.
        var cutoff = now - quietPeriod;
        await ctx.ScanBatches
            .Where(b => b.Status == ScanBatchStatus.Collecting && b.LastFileUtc <= cutoff)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.Status, ScanBatchStatus.Matching), ct);

        // Matching → Ready once nothing open is still pending.
        await ctx.ScanBatches
            .Where(b => b.Status == ScanBatchStatus.Matching
                        && !ctx.ScanBatchItems.Any(i => i.ScanBatchId == b.Id
                                                        && i.Status == ScanBatchItemStatus.Pending
                                                        && i.State == ScanBatchItemState.Open))
            .ExecuteUpdateAsync(u => u
                .SetProperty(b => b.Status, ScanBatchStatus.Ready)
                .SetProperty(b => b.ReadyUtc, now), ct);

        var next = await (
                from i in ctx.ScanBatchItems.AsNoTracking()
                join b in ctx.ScanBatches.AsNoTracking() on i.ScanBatchId equals b.Id
                where b.Status == ScanBatchStatus.Matching
                      && i.Status == ScanBatchItemStatus.Pending
                      && i.State == ScanBatchItemState.Open
                orderby b.Id, i.Sequence
                select new { Item = i, Batch = b })
            .FirstOrDefaultAsync(ct);
        if (next is null)
            return false;

        var item = next.Item;
        var batch = next.Batch;
        try
        {
            var bytes = await File.ReadAllBytesAsync(storage.ItemPath(batch.Id, item.StoredFileName), ct);
            var match = await matcher.MatchAsync(bytes, batch.Game, item.IsFoil, batch.SetCodeList, batch.Language, ct);
            if (match.Matched && !string.IsNullOrEmpty(match.GameCardId))
                match = match with { IsNew = isNewCard(batch.Game, match.GameCardId) };

            // Browsers can't show TIFF, so keep a JPEG preview beside it (as a file, not a data URI in the row).
            string? previewName = null;
            if (ScanBatchStorage.IsTiff(item.StoredFileName) && WebScanMatchingService.RenderPreviewJpeg(bytes) is { } jpeg)
            {
                previewName = $"{item.Id}.preview.jpg";
                await File.WriteAllBytesAsync(storage.ItemPath(batch.Id, previewName), jpeg, ct);
            }
            match = match with { ScanPreviewDataUri = null };

            var json = JsonSerializer.Serialize(match, ScanBatchStorage.Json);
            var include = match.Matched;
            var language = match.Language;
            // Only a still-pending row is written: a reviewer who removed the item meanwhile wins.
            await ctx.ScanBatchItems
                .Where(i => i.Id == item.Id && i.Status == ScanBatchItemStatus.Pending)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(i => i.MatchJson, json)
                    .SetProperty(i => i.Status, ScanBatchItemStatus.Matched)
                    .SetProperty(i => i.Error, (string?)null)
                    .SetProperty(i => i.Include, include)
                    .SetProperty(i => i.Language, i => language ?? i.Language)
                    .SetProperty(i => i.PreviewFileName, previewName), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Matching scan {File} in batch {Batch} failed", item.OriginalFileName, batch.Name);
            var message = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            await ctx.ScanBatchItems
                .Where(i => i.Id == item.Id && i.Status == ScanBatchItemStatus.Pending)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(i => i.Status, ScanBatchItemStatus.Error)
                    .SetProperty(i => i.Error, message), CancellationToken.None);
        }
        return true;
    }
}
