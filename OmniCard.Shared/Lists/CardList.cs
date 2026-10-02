using OmniCard.Shared.Cards;
namespace OmniCard.Shared.Lists;

public enum ListItemSource { Manual, Url, Paste, File, Scan }

public class CardList
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public CardGame Game { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    /// <summary>Forced card language (a <c>CardLanguages</c> code), or null for any. When set, only owned
    /// copies in this language count as owned, and cards the collection is missing are created in it.
    /// When null, a copy in any language counts and missing cards are created in English.</summary>
    public string? Language { get; set; }

    /// <summary>The Moxfield / Archidekt URL the list was imported from, used by "update from URL".</summary>
    public string? SourceUrl { get; set; }
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
    /// <summary>Foil finish sub-type carried to the created lot on commit; null for non-foil. See <see cref="FoilTypes"/>.</summary>
    public string? FoilType { get; set; }

    /// <summary>Market price captured when the printing was resolved; null if unpriced.</summary>
    public decimal? AddedMarketPrice { get; set; }
    /// <summary>True when no printing had a price and a fallback printing was chosen.</summary>
    public bool IsUnpriced { get; set; }

    /// <summary>When this item was added by picking an owned copy from the collection, the id of the
    /// source <c>InventoryLot</c>. Adding to a list never moves or mutates that lot; the reference is
    /// only consumed at commit time, where the referenced copies are <em>relocated</em> to the target
    /// location instead of creating brand-new lots. Null for catalog / URL / name-resolved items, which
    /// commit as newly-created lots.</summary>
    public int? SourceLotId { get; set; }

    /// <summary>Set when this item's owned copies were already moved out by a list fulfillment and only
    /// the remainder (still to buy) is left. Such an item is no longer matched against the collection, or
    /// the copies that were just moved would be counted against the quantity still needed.</summary>
    public bool AwaitingPurchase { get; set; }

    /// <summary>Set on an owned copy of a different printing that stands in for a card the collection
    /// didn't have (approved from "find in collection"): the game card id of the card it replaces. An
    /// update from the source URL counts this item toward that card, so it isn't added back.</summary>
    public string? SubstituteForCardId { get; set; }

    public ListItemSource Source { get; set; }
}

public enum ListUpdateKind { Add, Remove, Change }

/// <summary>One difference between a list and a fresh fetch of its source deck, keyed by printing
/// (<see cref="GameCardId"/> + <see cref="IsFoil"/>). Applying it sets that printing's total quantity on
/// the list to <see cref="NewQuantity"/> (0 removes it). <see cref="HandAdded"/> marks a removal of a card
/// that wasn't imported from a URL (added by hand or as a substitute), which the review leaves unticked.
/// <see cref="OwnedQuantity"/> is how many copies the collection already covers for an added card.</summary>
public record ListUpdateRow(
    ListUpdateKind Kind,
    string GameCardId,
    string CardName,
    string? SetCode,
    string? SetName,
    string? CollectorNumber,
    string? Rarity,
    string? ImageUri,
    bool IsFoil,
    int OldQuantity,
    int NewQuantity,
    decimal? Price,
    bool HandAdded = false,
    int OwnedQuantity = 0);

/// <summary>The changes an update from the source URL would make, for the user to review.</summary>
public record ListUpdatePreview(
    string DeckName,
    IReadOnlyList<ListUpdateRow> Rows,
    int UnchangedCount,
    IReadOnlyList<string> UnresolvedNames);

/// <summary>An approved stand-in: take <see cref="Quantity"/> copies of item <see cref="ItemId"/> and point
/// them at owned lot <see cref="LotId"/> (a different printing of the same card).</summary>
public record ListSubstitution(int ItemId, int LotId, int Quantity);

/// <summary>Takes <see cref="Quantity"/> copies off a list item once they've been moved or added to a
/// location. With <see cref="MarkAwaitingPurchase"/>, any remainder is flagged
/// <see cref="CardListItem.AwaitingPurchase"/> (see <c>IListService.ConsumeItems</c>).</summary>
public record ListItemConsumption(int ItemId, int Quantity, bool MarkAwaitingPurchase = false);

public record AddCardsResult(int AddedCount, IReadOnlyList<string> UnresolvedNames);

/// <summary>Result of committing a <see cref="CardList"/>'s items into real inventory at a location
/// (see <c>IListService.CommitToLocation</c>). Items that fail to re-resolve stay in the list.</summary>
public record CommitToLocationResult(int AddedCount, int RemainingUnresolvedCount, bool ListDeleted);
