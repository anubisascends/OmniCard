namespace OmniCard.Models;

public class MoveToLocationResult
{
    public required StorageContainer Container { get; init; }
    public string? Section { get; init; }
}
