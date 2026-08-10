namespace OmniCard.Models;

public class MoveListToLocationResult
{
    public StorageContainer? ExistingContainer { get; init; }
    public bool CreateNew { get; init; }
    public string NewContainerName { get; init; } = "";
    public ContainerType NewContainerType { get; init; }
    public required string Condition { get; init; }
}
