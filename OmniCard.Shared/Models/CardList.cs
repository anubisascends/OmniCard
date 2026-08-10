namespace OmniCard.Models;

public enum ListItemSource { Manual, Url, Paste, File, Scan }

public class CardList
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public CardGame Game { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}

public class CardListItem
{
    public int Id { get; set; }
    public int CardListId { get; set; }
    public int Quantity { get; set; } = 1;

    // Frozen printing (resolved at add/refresh time)
    public string GameCardId { get; set; } = "";
    public string CardName { get; set; } = "";
    public string? SetCode { get; set; }
    public string? CollectorNumber { get; set; }
    public bool IsFoil { get; set; }

    /// <summary>Market price captured when the printing was resolved; null if unpriced.</summary>
    public decimal? AddedMarketPrice { get; set; }
    /// <summary>True when no printing had a price and a fallback printing was chosen.</summary>
    public bool IsUnpriced { get; set; }

    public ListItemSource Source { get; set; }
}

public record AddCardsResult(int AddedCount, IReadOnlyList<string> UnresolvedNames);

/// <summary>Result of committing a <see cref="CardList"/>'s items into real inventory at a location
/// (see <c>IListService.CommitToLocation</c>). Items that fail to re-resolve stay in the list.</summary>
public record CommitToLocationResult(int AddedCount, int RemainingUnresolvedCount, bool ListDeleted);
