namespace OmniCard.Shared.Storage;

/// <summary>A card the deck box's linked decklist calls for that the user marked "not in collection"
/// during a sync — copies they don't own yet. One row per card name per deck box, holding how many copies
/// are short. Each sync replaces the box's rows with what the user marked that time, so a card that's
/// been found, bought or dropped from the deck stops being reported.</summary>
public class DeckBoxMissingCard
{
    public int Id { get; set; }
    public int ContainerId { get; set; }
    public string CardName { get; set; } = "";
    public string? SetCode { get; set; }
    public string? CollectorNumber { get; set; }
    public int Quantity { get; set; }
    public DateTime MarkedAt { get; set; } = DateTime.UtcNow;

    public const int MaxCardNameLength = 300;
}
