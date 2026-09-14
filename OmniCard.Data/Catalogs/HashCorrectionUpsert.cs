using Microsoft.EntityFrameworkCore;
using OmniCard.Shared.Matching;

namespace OmniCard.Data.Catalogs;

/// <summary>
/// Provider-agnostic upsert of a scan-hash → correct-card mapping into a catalog's HashCorrections
/// table (keyed by the unique <see cref="HashCorrection.ScanHash"/>). Replaces the per-service
/// SQLite-only <c>INSERT OR REPLACE</c>, which threw on the web app's SQL Server catalogs — so
/// corrections silently stopped being recorded once matching moved server-side.
/// </summary>
public static class HashCorrectionUpsert
{
    /// <summary>Insert or update the correction for <paramref name="scanHash"/>. The existing row is
    /// located by matching the hash IN MEMORY: ulong equality doesn't translate to SQL uniformly
    /// across SQLite (desktop/tests) and SQL Server (web), and the codebase compares perceptual hashes
    /// in memory throughout. The corrections table holds only user edits, so it stays small.</summary>
    /// <param name="enrich">Optional hook to set extra columns (e.g. Scryfall's identifying
    /// name/set/number used to relink corrections after a catalog refresh).</param>
    public static void Upsert(DbContext ctx, ulong scanHash, string correctCardId, ulong? artScanHash,
        Action<HashCorrection>? enrich = null)
    {
        var set = ctx.Set<HashCorrection>();
        var existingId = set.AsNoTracking()
            .Select(h => new { h.Id, h.ScanHash })
            .AsEnumerable()
            .FirstOrDefault(h => h.ScanHash == scanHash)?.Id;

        HashCorrection row;
        if (existingId is int id)
        {
            row = set.First(h => h.Id == id);
        }
        else
        {
            row = new HashCorrection { ScanHash = scanHash };
            set.Add(row);
        }

        row.CorrectCardId = correctCardId;
        row.ArtScanHash = artScanHash;
        row.CreatedAt = DateTime.UtcNow;
        enrich?.Invoke(row);
        ctx.SaveChanges();
    }
}
