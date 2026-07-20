namespace OmniCard.Models;

public record PickListEntry(
    int LotId,
    string Name,
    string SetName,
    string? Condition,
    bool IsFoil,
    string LocationName,
    string? Section,
    int? Page,
    int? Slot,
    decimal ListedPrice,
    int Quantity);
