namespace OmniCard.Models;

/// <summary>Full detail of a Listed/Picked listing for the Manage Listings screen — unlike
/// <see cref="ActiveListing"/> (a lean projection for the order-line picker), this carries the
/// <see cref="Listing"/> id and every editable sale property so a user can adjust price, channel,
/// quantity, or note after the fact (e.g. when the external sale site's price changed).</summary>
public record ListingDetail(
    int Id,
    int LotId,
    string Name,
    string SetName,
    string SetCode,
    string? Condition,
    bool IsFoil,
    SalesChannel Channel,
    ListingStatus Status,
    decimal ListedPrice,
    int Quantity,
    string? Note,
    DateTime ListedAt,
    DateTime? PickedAt);
