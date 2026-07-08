using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public sealed class MismatchLogService(
    IDbContextFactory<CollectionDbContext> dbContextFactory) : IMismatchLogService
{
    public async Task LogMismatchAsync(CardMatch oldMatch, CardMatch newMatch, ScannedCard scannedCard)
    {
        if (oldMatch.Confidence is not >= 80) return;
        if (oldMatch.GameSpecificId == newMatch.GameSpecificId) return;

        using var ctx = dbContextFactory.CreateDbContext();
        ctx.MismatchLogs.Add(new MismatchLog
        {
            ScanHash = scannedCard.Hash,
            ScanImagePath = scannedCard.TempImagePath,
            OriginalCardId = oldMatch.GameSpecificId,
            OriginalName = oldMatch.Name,
            OriginalSetCode = oldMatch.SetCode,
            OriginalNumber = oldMatch.CollectorNumber,
            OriginalConfidence = oldMatch.Confidence ?? 0,
            CorrectedCardId = newMatch.GameSpecificId,
            CorrectedName = newMatch.Name,
            CorrectedSetCode = newMatch.SetCode,
            CorrectedNumber = newMatch.CollectorNumber,
        });
        await ctx.SaveChangesAsync();
    }
}
