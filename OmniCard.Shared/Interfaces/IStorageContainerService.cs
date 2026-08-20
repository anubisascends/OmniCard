using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IStorageContainerService
{
    List<StorageContainer> GetAll();
    StorageContainer GetBulk();
    StorageContainer Create(string name, ContainerType type, int slotsPerPage = 9);
    void Rename(int id, string newName);
    void Delete(int id, bool moveCardsToBulk = true);
    int GetCardCount(int containerId);
    void SetCoverCard(int containerId, int? cardId);
    List<CollectionCard> GetCardsInContainer(int containerId);
    void SetExcludeFromDeckCheck(int containerId, bool exclude);

    /// <summary>Marks a location as "always available": grouped with the system Bulk location in the
    /// collection overview and never hidden by the active game filter. No-op semantics for the
    /// system Bulk location, which is always available regardless.</summary>
    void SetAlwaysAvailable(int containerId, bool alwaysAvailable);

    BinderLayout GetBinderLayout(int containerId);

    /// <summary>Appends a new physical sheet (leaf) to the end of the binder.
    /// <paramref name="doubleSided"/> true (the default in the UI) adds a front and a back — two
    /// logical pages; false adds a single-sided sheet — one logical page — for a single-pocket
    /// page or when the back isn't wanted.</summary>
    void AddBinderSheet(int containerId, bool doubleSided);

    /// <summary>Describes the physical sheet that owns the given 1-based logical page — its page
    /// range, side count, position, and how many cards it currently holds. For remove/insert/move
    /// confirmations and pickers.</summary>
    BinderSheetInfo GetSheetForPage(int containerId, int page);

    /// <summary>All physical sheets of the binder in reading order, each with its page range, side
    /// count, and card count — for the insert/move position pickers.</summary>
    List<BinderSheetInfo> GetSheets(int containerId);

    /// <summary>Inserts a new empty sheet at <paramref name="insertIndex"/> (0 = before the first
    /// sheet, sheet-count = at the end). <paramref name="doubleSided"/> adds a front and back (two
    /// pages) or a single page. Every page at or after the insertion point shifts up.</summary>
    void InsertBinderSheet(int containerId, int insertIndex, bool doubleSided);

    /// <summary>Moves the sheet that owns <paramref name="fromPage"/> to <paramref name="toIndex"/>
    /// — an insertion index into the list of the other sheets (0 = before the first, sheet-count − 1
    /// = at the end). Every card on a page whose number changes is renumbered (slots preserved);
    /// nothing is unplaced. Mirrors pulling a page out of a binder and slotting it in elsewhere.</summary>
    void MoveBinderSheet(int containerId, int fromPage, int toIndex);

    /// <summary>Removes the physical sheet that owns the given 1-based logical page (both sides of a
    /// double-sided sheet). Cards on the removed sheet are returned to the binder's Unplaced pool;
    /// every trailing page shifts down to close the gap. Throws if the page isn't in the binder or
    /// it's the binder's only sheet.</summary>
    void RemoveBinderSheet(int containerId, int page);

    void SetSlotsPerPage(int containerId, int slotsPerPage);
    void SetColumns(int containerId, int columns);
    List<CollectionCard> GetPlacedCardsOnPage(int containerId, int page);
    void AssignCardToSlot(int lotId, int containerId, int page, int slot);

    /// <summary>Clears a card's page/slot placement, returning it to the binder's Unplaced Cards
    /// pool without moving it out of the container.</summary>
    void UnassignFromPage(int lotId);
}
