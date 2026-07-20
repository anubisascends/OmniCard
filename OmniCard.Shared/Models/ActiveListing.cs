namespace OmniCard.Models;

public record ActiveListing(
    int LotId,
    string Name,
    string SetName,
    string SetCode,
    string? Condition,
    bool IsFoil,
    decimal ListedPrice,
    ListingStatus Status);
