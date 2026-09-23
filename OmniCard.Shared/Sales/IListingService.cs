using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Sales;

public interface IListingService
{
    int ListForSale(IEnumerable<int> lotIds, SalesChannel channel, decimal price, int quantity, string? note = null);

    /// <summary>Lists <paramref name="quantity"/> copies of a single lot for sale. When the lot holds more
    /// than <paramref name="quantity"/>, it is first split so a sibling lot of exactly that quantity is
    /// created in the same location and only that lot is listed — this keeps a later pick/move from moving
    /// the copies that weren't sold. Returns the listed (possibly newly split) lot id, or 0 if the lot
    /// wasn't found. Throws <see cref="ArgumentOutOfRangeException"/> if quantity is outside 1..lot quantity.</summary>
    int ListForSaleSplitting(int lotId, SalesChannel channel, decimal price, int quantity, string? note = null);

    /// <summary>Merges <paramref name="quantity"/> copies from <paramref name="sourceLotId"/> into the
    /// lot behind <paramref name="targetLotId"/>'s active listing — used when listing a card identical to
    /// one already listed (eBay treats them as one multi-quantity listing). Moves the physical copies
    /// (removing the source lot if fully consumed) and increases the target listing's quantity, which it
    /// returns. Throws if the source/target lot or the target's active listing can't be found, or if
    /// quantity is outside 1..source quantity.</summary>
    int MergeIntoListing(int sourceLotId, int quantity, int targetLotId);
    void Unlist(IEnumerable<int> lotIds);
    int MarkPicked(IEnumerable<int> lotIds);
    List<PickListEntry> GetPickList(CardGame? game = null);
    Dictionary<int, ListingStatus> GetActiveListingStatusByLot(IEnumerable<int> lotIds);
    List<ActiveListing> GetActiveListings(CardGame? game = null);
    void MarkSold(int lotId, int orderLineId);

    /// <summary>Full detail (including Id and every editable sale property) for every Listed/Picked
    /// listing, for the Manage Listings screen.</summary>
    List<ListingDetail> GetListingDetails(CardGame? game = null);

    /// <summary>Updates the sale properties of an existing Listed/Picked listing in place — does not
    /// touch inventory location or status. Use <see cref="Unlist"/> to cancel a listing instead.</summary>
    void UpdateListing(int listingId, decimal price, SalesChannel channel, int quantity, string? note);
}
