using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IStorageContainerService
{
    List<StorageContainer> GetAll();
    StorageContainer GetBulk();
    StorageContainer Create(string name, ContainerType type);
    void Rename(int id, string newName);
    void Delete(int id, bool moveCardsToBulk = true);
    int GetCardCount(int containerId);
    void SetCoverCard(int containerId, int? cardId);
    List<CollectionCard> GetCardsInContainer(int containerId);
    void SetExcludeFromDeckCheck(int containerId, bool exclude);
}
