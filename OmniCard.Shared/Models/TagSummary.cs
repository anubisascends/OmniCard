namespace OmniCard.Models;

/// <summary>Display-ready tag with a computed usage count, for the tag library / autocomplete.</summary>
public class TagSummary
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int UsageCount { get; init; }
}
