using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IListService
{
    IReadOnlyList<CardList> GetLists(CardGame game);
    CardList CreateList(string name, CardGame game);
    void RenameList(int listId, string name);
    void DeleteList(int listId);

    IReadOnlyList<CardListItem> GetItems(int listId);
    CardListItem AddPrinting(int listId, CardMatch printing, bool isFoil, string? foilType, int quantity, ListItemSource source);
    void RemoveItem(int itemId);
    void SetQuantity(int itemId, int quantity);

    // Implemented in Task 3
    AddCardsResult AddCardsByName(int listId, IEnumerable<DecklistEntry> entries);
    void RefreshPrices(int listId);
    List<DecklistEntry> ToDecklistEntries(int listId);

    /// <summary>Commits the list's items into real inventory at <paramref name="container"/>, creating a
    /// Product+InventoryLot per item (respecting each item's foil flag and quantity). Committed items are
    /// removed from the list; items that fail to re-resolve to a printing are left behind. The list itself
    /// is deleted once it has no items remaining.</summary>
    CommitToLocationResult CommitToLocation(int listId, StorageContainer container, string condition);
}
