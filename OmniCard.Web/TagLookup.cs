using Microsoft.EntityFrameworkCore;
using OmniCard.Data;

namespace OmniCard.Web;

/// <summary>
/// Read-only batch tag lookup for the web companion. Queries LotTags/Tags directly rather than
/// going through OmniCard.Collection's ITagService, since that service's write methods
/// (rename/delete/merge/SaveChanges) have no place in this read-only app.
/// </summary>
public static class TagLookup
{
    public static Dictionary<int, List<string>> GetTagsByLots(OmniCardDbContext db, IEnumerable<int> lotIds)
    {
        var ids = lotIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return db.LotTags.AsNoTracking()
            .Include(lt => lt.Tag)
            .Where(lt => ids.Contains(lt.LotId))
            .ToList()
            .GroupBy(lt => lt.LotId)
            .ToDictionary(g => g.Key, g => g.Select(lt => lt.Tag.Name).OrderBy(n => n).ToList());
    }
}
