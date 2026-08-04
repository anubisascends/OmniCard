namespace OmniCard.Models;

/// <summary>Display-ready view of a <see cref="Trade"/> for the "Link to Trade" picker — includes
/// a computed replacement count so the user can see whether/how much a trade has already been
/// fulfilled without a separate query.</summary>
public class TradeSummary
{
    public int Id { get; init; }
    public CardGame Game { get; init; }
    public string CardName { get; init; } = "";
    public string? SetCode { get; init; }
    public string? SetName { get; init; }
    public string? CollectorNumber { get; init; }
    public bool Foil { get; init; }
    public string Note { get; init; } = "";
    public string? PhotoPath { get; init; }
    public DateTime CreatedAt { get; init; }
    public int ReplacementCount { get; init; }
}
