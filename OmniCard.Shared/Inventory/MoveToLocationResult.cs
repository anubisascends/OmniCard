using OmniCard.Shared.Storage;
namespace OmniCard.Shared.Inventory;

public class MoveToLocationResult
{
    public required StorageContainer Container { get; init; }
    public string? Section { get; init; }
}
