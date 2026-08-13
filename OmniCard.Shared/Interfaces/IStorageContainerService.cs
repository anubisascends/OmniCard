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

    BinderLayout GetBinderLayout(int containerId);
    void AddBinderPage(int containerId);
    void SetSlotsPerPage(int containerId, int slotsPerPage);
    void SetColumns(int containerId, int columns);
    List<CollectionCard> GetPlacedCardsOnPage(int containerId, int page);
    void AssignCardToSlot(int lotId, int containerId, int page, int slot);

    /// <summary>Clears a card's page/slot placement, returning it to the binder's Unplaced Cards
    /// pool without moving it out of the container.</summary>
    void UnassignFromPage(int lotId);
}
