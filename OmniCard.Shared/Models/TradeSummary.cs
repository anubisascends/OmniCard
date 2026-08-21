namespace OmniCard.Models;

/// <summary>Display-ready view of a <see cref="TradeSession"/> for the "Link to Trade" picker and
/// the desktop Trades history view — includes the outgoing cards, the received note/photo, a value
/// delta, and a computed replacement count so the user can see whether/how much the trade has
/// already been fulfilled without a separate query.</summary>
public class TradeSummary
{
    public int Id { get; init; }
    public string Note { get; init; } = "";
    public string? ReceivedPhotoPath { get; init; }
    public decimal OutgoingValue { get; init; }
    public decimal? ReceivedValue { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? FirstFulfilledAt { get; init; }
    public int ReplacementCount { get; init; }

    public List<TradeCardSummary> OutgoingCards { get; init; } = [];

    /// <summary>Difference between received and outgoing value, when the received value is known.</summary>
    public decimal? ValueDelta => ReceivedValue is { } rv ? rv - OutgoingValue : null;

    /// <summary>Compact label for the picker/badge, e.g. "Sol Ring" or "Sol Ring (+2 more)".</summary>
    public string Label
    {
        get
        {
            if (OutgoingCards.Count == 0) return "(empty trade)";
            var first = OutgoingCards[0].CardName;
            if (string.IsNullOrWhiteSpace(first)) first = "(card)";
            return OutgoingCards.Count == 1 ? first : $"{first} (+{OutgoingCards.Count - 1} more)";
        }
    }

    /// <summary>Thumbnail for the picker tile — the received photo if present, else the first
    /// outgoing card's photo.</summary>
    public string? ThumbnailPath => ReceivedPhotoPath
        ?? OutgoingCards.Select(c => c.PhotoPath).FirstOrDefault(p => !string.IsNullOrEmpty(p));
}

/// <summary>One outgoing card within a <see cref="TradeSummary"/>.</summary>
public class TradeCardSummary
{
    public CardGame Game { get; init; }
    public string CardName { get; init; } = "";
    public string? SetCode { get; init; }
    public string? SetName { get; init; }
    public string? CollectorNumber { get; init; }
    public bool Foil { get; init; }
    public bool IsOffDatabase { get; init; }
    public decimal? EstimatedValue { get; init; }

    /// <summary>Photo path for this outgoing card (off-database photo, or the traded-away lot's
    /// scan image when still available).</summary>
    public string? PhotoPath { get; init; }
}
