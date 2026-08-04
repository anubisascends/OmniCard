using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface ITagService
{
    /// <summary>Every tag, alphabetical, with a computed usage count.</summary>
    List<TagSummary> GetAllTags();

    List<string> GetTagsForLot(int lotId);

    /// <summary>Batch form of <see cref="GetTagsForLot"/> for populating a page of results
    /// without one query per row. Lots with no tags are omitted from the result.</summary>
    Dictionary<int, List<string>> GetTagsByLots(IEnumerable<int> lotIds);

    /// <summary>Replaces the full tag set on a lot — creates any tag name that doesn't already
    /// exist (case-insensitive reuse), removes any no-longer-present tag from the lot.</summary>
    void SetTagsForLot(int lotId, IEnumerable<string> tagNames);

    /// <summary>Adds one tag to every listed lot (creating the tag if it doesn't exist yet).
    /// A lot that already has the tag is left alone.</summary>
    void AddTagToLots(IEnumerable<int> lotIds, string tagName);

    void RenameTag(int tagId, string newName);

    /// <summary>Removes the tag from every lot and deletes it.</summary>
    void DeleteTag(int tagId);

    /// <summary>Reassigns every lot tagged with <paramref name="sourceTagId"/> to
    /// <paramref name="targetTagId"/> (skipping lots that already have the target), then deletes
    /// the source tag.</summary>
    void MergeTags(int sourceTagId, int targetTagId);
}
