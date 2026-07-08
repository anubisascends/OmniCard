namespace OmniCard.Models;

public class RelatedCard
{
    public int Id { get; set; }
    public Guid CardId { get; set; }
    public Guid ScryfallId { get; set; }
    public string Component { get; set; } = "";
    public string Name { get; set; } = "";
    public string TypeLine { get; set; } = "";
    public string Uri { get; set; } = "";
    public Card Card { get; set; } = null!;
}
