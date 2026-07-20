using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IListingService
{
    int ListForSale(IEnumerable<int> lotIds, SalesChannel channel, decimal price, int quantity, string? note = null);
    void Unlist(IEnumerable<int> lotIds);
    int MarkPicked(IEnumerable<int> lotIds);
    List<PickListEntry> GetPickList(CardGame? game = null);
    Dictionary<int, ListingStatus> GetActiveListingStatusByLot(IEnumerable<int> lotIds);
}
