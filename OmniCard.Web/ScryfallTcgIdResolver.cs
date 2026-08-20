using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;

namespace OmniCard.Web;

/// <summary>
/// Resolves MTG cards' real TCGplayer product ids from the read-only <c>scryfall.db</c> catalog.
/// The owned-collection store only keeps the Scryfall id (a GUID, in <c>Product.GameCardId</c>);
/// the TCGplayer product id lives on the catalog <see cref="OmniCard.Models.Card"/>. Batched so a
/// binder page of many MTG cards is a single query. Missing/locked catalog DBs degrade to empty
/// (no link deep-linking), matching the SqliteException-swallowing pattern used for extended data.
/// </summary>
public static class ScryfallTcgIdResolver
{
    /// <summary>Plain and etched TCGplayer product ids for one catalog card.</summary>
    public readonly record struct Ids(int? Tcgplayer, int? TcgplayerEtched)
    {
        /// <summary>Best product id for a card: etched foils prefer the etched id, others the plain
        /// id, each falling back to whichever is present.</summary>
        public int? Pick(bool etched) => etched ? (TcgplayerEtched ?? Tcgplayer) : (Tcgplayer ?? TcgplayerEtched);
    }

    /// <summary>Map from the original <c>GameCardId</c> string to its catalog TCGplayer ids. Only
    /// entries whose id parses as a Guid and matches a catalog row are included.</summary>
    public static Dictionary<string, Ids> Resolve(
        IDbContextFactory<ScryfallDbContext>? factory, IEnumerable<string> gameCardIds)
    {
        var result = new Dictionary<string, Ids>();
        if (factory is null)
            return result;

        // Keep the mapping from parsed Guid back to the caller's original string key.
        var byGuid = new Dictionary<Guid, string>();
        foreach (var gcid in gameCardIds)
            if (Guid.TryParse(gcid, out var guid))
                byGuid[guid] = gcid;

        if (byGuid.Count == 0)
            return result;

        try
        {
            using var db = factory.CreateDbContext();
            var guids = byGuid.Keys.ToList();
            var rows = db.Cards.AsNoTracking()
                .Where(c => guids.Contains(c.Id))
                .Select(c => new { c.Id, c.TcgplayerId, c.TcgplayerEtchedId })
                .ToList();

            foreach (var row in rows)
                result[byGuid[row.Id]] = new Ids(row.TcgplayerId, row.TcgplayerEtchedId);
        }
        catch (SqliteException)
        {
            // scryfall.db missing, locked, or corrupt — fall back to search links.
        }

        return result;
    }
}
