using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IListService
{
    IReadOnlyList<CardList> GetLists(CardGame game);
    CardList CreateList(string name, CardGame game);
    void RenameList(int listId, string name);
    void DeleteList(int listId);

    IReadOnlyList<CardListItem> GetItems(int listId);
    CardListItem AddPrinting(int listId, CardMatch printing, bool isFoil, int quantity, ListItemSource source);
    void RemoveItem(int itemId);
    void SetQuantity(int itemId, int quantity);

    // Implemented in Task 3
    AddCardsResult AddCardsByName(int listId, IEnumerable<DecklistEntry> entries);
    void RefreshPrices(int listId);
    List<DecklistEntry> ToDecklistEntries(int listId);
}
