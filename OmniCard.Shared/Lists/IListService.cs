using OmniCard.Shared.Cards;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Storage;

namespace OmniCard.Shared.Lists;

public interface IListService
{
    IReadOnlyList<CardList> GetLists(CardGame game);
    CardList CreateList(string name, CardGame game);
    void RenameList(int listId, string name);
    void DeleteList(int listId);

    IReadOnlyList<CardListItem> GetItems(int listId);
    CardListItem AddPrinting(int listId, CardMatch printing, bool isFoil, string? foilType, int quantity, ListItemSource source, int? sourceLotId = null);

    /// <summary>Adds a card the user already owns to the list by referencing an existing <c>InventoryLot</c>.
    /// The printing is frozen from the lot's product; the lot itself is <em>not</em> moved or mutated — the
    /// reference is only acted on at commit time (where the copies are relocated instead of duplicated).</summary>
    CardListItem AddOwnedLot(int listId, int lotId, int quantity);
    void RemoveItem(int itemId);
    void SetQuantity(int itemId, int quantity);

    /// <summary>Adds each decklist entry to the list, resolving to the exact printing named by the entry's
    /// set + collector number when present (falling back to the cheapest printing by name otherwise). The
    /// <paramref name="source"/> is stamped on new items and governs how <see cref="RefreshPrices"/> treats
    /// them: <see cref="ListItemSource.Url"/> (and Manual) items keep their frozen printing on refresh, while
    /// name-resolved sources re-track the current cheapest printing.</summary>
    AddCardsResult AddCardsByName(int listId, IEnumerable<DecklistEntry> entries, ListItemSource source = ListItemSource.Paste);
    void RefreshPrices(int listId);
    List<DecklistEntry> ToDecklistEntries(int listId);

    /// <summary>Commits the list's items into real inventory at <paramref name="container"/>, creating a
    /// Product+InventoryLot per item (respecting each item's foil flag and quantity). Committed items are
    /// removed from the list; items that fail to re-resolve to a printing are left behind. The list itself
    /// is deleted once it has no items remaining.</summary>
    CommitToLocationResult CommitToLocation(int listId, StorageContainer container, string condition);
}
