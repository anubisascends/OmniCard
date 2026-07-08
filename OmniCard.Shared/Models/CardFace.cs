namespace OmniCard.Models;

public class CardFace
{
    public string Name { get; set; } = "";
    public string ManaCost { get; set; } = "";
    public string TypeLine { get; set; } = "";
    public string? OracleText { get; set; }
    public List<string>? Colors { get; set; }
    public List<string>? ColorIndicator { get; set; }
    public string? Power { get; set; }
    public string? Toughness { get; set; }
    public string? Loyalty { get; set; }
    public string? Defense { get; set; }
    public string? FlavorText { get; set; }
    public string? Artist { get; set; }
    public Guid? ArtistId { get; set; }
    public Guid? IllustrationId { get; set; }
    public ImageUris? ImageUris { get; set; }
    public string? Watermark { get; set; }
    public string? PrintedName { get; set; }
    public string? PrintedTypeLine { get; set; }
    public string? PrintedText { get; set; }
}
