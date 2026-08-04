namespace OmniCard.Models;

/// <summary>Join row: one tag applied to one physical copy.</summary>
public class LotTag
{
    public int Id { get; set; }
    public int LotId { get; set; }
    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
