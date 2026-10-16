using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Scanning;

/// <summary>
/// A saved scan validation rule: a named search query (search syntax, checked by the web layer) for one
/// <see cref="Game"/>. On the Scan page a reviewer loads one (or types a query), presses Validate, and
/// every scanned card that does <b>not</b> match the query is flagged. Rules only ever read — they never
/// change a scan — so they're shared by everyone who scans, and names are unique per game.
/// </summary>
public class ScanValidationRule
{
    public const int MaxNameLength = 100;
    public const int MaxQueryLength = 1000;

    public int Id { get; set; }

    public CardGame Game { get; set; }

    public string Name { get; set; } = "";

    public string Query { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
