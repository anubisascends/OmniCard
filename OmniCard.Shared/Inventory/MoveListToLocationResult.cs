using OmniCard.Shared.Storage;
namespace OmniCard.Shared.Inventory;

public class MoveListToLocationResult
{
    public StorageContainer? ExistingContainer { get; init; }
    public bool CreateNew { get; init; }
    public string NewContainerName { get; init; } = "";
    public ContainerType NewContainerType { get; init; }
    public required string Condition { get; init; }
}
