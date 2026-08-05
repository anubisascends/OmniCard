using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public sealed class TagService(IDbContextFactory<OmniCardDbContext> dbContextFactory) : ITagService
{
    public List<TagSummary> GetAllTags()
    {
        using var context = dbContextFactory.CreateDbContext();
        var counts = context.LotTags.AsNoTracking()
            .GroupBy(lt => lt.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .ToDictionary(x => x.TagId, x => x.Count);

        return context.Tags.AsNoTracking()
            .OrderBy(t => t.Name)
            .ToList()
            .Select(t => new TagSummary { Id = t.Id, Name = t.Name, UsageCount = counts.GetValueOrDefault(t.Id) })
            .ToList();
    }

    public List<string> GetTagsForLot(int lotId)
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.LotTags.AsNoTracking()
            .Where(lt => lt.LotId == lotId)
            .Include(lt => lt.Tag)
            .Select(lt => lt.Tag.Name)
            .OrderBy(n => n)
            .ToList();
    }

    public Dictionary<int, List<string>> GetTagsByLots(IEnumerable<int> lotIds)
    {
        var ids = lotIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        using var context = dbContextFactory.CreateDbContext();
        return CardService.ChunkedByIdLookup(
                ids,
                chunk => context.LotTags.AsNoTracking().Include(lt => lt.Tag).Where(lt => chunk.Contains(lt.LotId)).ToList(),
                lt => lt.Id)
            .Values
            .GroupBy(lt => lt.LotId)
            .ToDictionary(g => g.Key, g => g.Select(lt => lt.Tag.Name).OrderBy(n => n).ToList());
    }

    public void SetTagsForLot(int lotId, IEnumerable<string> tagNames)
    {
        // Keyed by lowercase for case-insensitive comparison, valued by the casing the caller
        // used (preserved for any newly-created tag).
        var wanted = tagNames
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .GroupBy(n => n.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        using var context = dbContextFactory.CreateDbContext();
        var existingLinks = context.LotTags.Include(lt => lt.Tag).Where(lt => lt.LotId == lotId).ToList();

        // Remove links for tags no longer wanted.
        foreach (var link in existingLinks.Where(l => !wanted.ContainsKey(l.Tag.Name.ToLowerInvariant())))
            context.LotTags.Remove(link);

        // Add links for wanted tags not already present.
        var existingLower = existingLinks.Select(l => l.Tag.Name.ToLowerInvariant()).ToHashSet();
        foreach (var (lower, original) in wanted.Where(kv => !existingLower.Contains(kv.Key)))
        {
            var tag = FindOrCreateTag(context, original);
            // Set via the Tag navigation, not the TagId scalar — a newly-created tag isn't
            // saved yet at this point, so tag.Id is still 0.
            context.LotTags.Add(new LotTag { LotId = lotId, Tag = tag });
        }

        context.SaveChanges();
    }

    public void AddTagToLots(IEnumerable<int> lotIds, string tagName)
    {
        var name = tagName.Trim();
        if (name.Length == 0) return;

        using var context = dbContextFactory.CreateDbContext();
        var tag = FindOrCreateTag(context, name);
        context.SaveChanges(); // ensure tag.Id is assigned before the membership check below

        var lotIdList = lotIds.Distinct().ToList();
        var alreadyTagged = context.LotTags.AsNoTracking()
            .Where(lt => lt.TagId == tag.Id && lotIdList.Contains(lt.LotId))
            .Select(lt => lt.LotId)
            .ToHashSet();

        foreach (var lotId in lotIdList.Where(id => !alreadyTagged.Contains(id)))
            context.LotTags.Add(new LotTag { LotId = lotId, TagId = tag.Id });

        context.SaveChanges();
    }

    public void RemoveTagFromLots(IEnumerable<int> lotIds, string tagName)
    {
        var name = tagName.Trim();
        if (name.Length == 0) return;

        using var context = dbContextFactory.CreateDbContext();
        var lotIdList = lotIds.Distinct().ToList();

        var links = context.LotTags
            .Include(lt => lt.Tag)
            .Where(lt => lotIdList.Contains(lt.LotId) && lt.Tag.Name.ToLower() == name.ToLower())
            .ToList();

        context.LotTags.RemoveRange(links);
        context.SaveChanges();
    }

    public void RenameTag(int tagId, string newName)
    {
        var name = newName.Trim();
        if (name.Length == 0) return;

        using var context = dbContextFactory.CreateDbContext();
        var tag = context.Tags.FirstOrDefault(t => t.Id == tagId);
        if (tag is null) return;

        tag.Name = name;
        context.SaveChanges();
    }

    public void DeleteTag(int tagId)
    {
        using var context = dbContextFactory.CreateDbContext();
        context.LotTags.RemoveRange(context.LotTags.Where(lt => lt.TagId == tagId));
        var tag = context.Tags.FirstOrDefault(t => t.Id == tagId);
        if (tag is not null)
            context.Tags.Remove(tag);
        context.SaveChanges();
    }

    public void MergeTags(int sourceTagId, int targetTagId)
    {
        if (sourceTagId == targetTagId) return;

        using var context = dbContextFactory.CreateDbContext();
        var targetLotIds = context.LotTags.AsNoTracking()
            .Where(lt => lt.TagId == targetTagId)
            .Select(lt => lt.LotId)
            .ToHashSet();

        var sourceLinks = context.LotTags.Where(lt => lt.TagId == sourceTagId).ToList();
        foreach (var link in sourceLinks)
        {
            if (targetLotIds.Contains(link.LotId))
                context.LotTags.Remove(link); // target already has it — drop the duplicate
            else
                link.TagId = targetTagId; // reassign
        }

        var sourceTag = context.Tags.FirstOrDefault(t => t.Id == sourceTagId);
        if (sourceTag is not null)
            context.Tags.Remove(sourceTag);

        context.SaveChanges();
    }

    private static Tag FindOrCreateTag(OmniCardDbContext context, string name)
    {
        var existing = context.Tags.FirstOrDefault(t => t.Name.ToLower() == name.ToLower());
        if (existing is not null)
            return existing;

        var tag = new Tag { Name = name };
        context.Tags.Add(tag);
        return tag;
    }
}
