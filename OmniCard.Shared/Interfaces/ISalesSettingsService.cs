namespace OmniCard.Interfaces;

public interface ISalesSettingsService
{
    int? ForSaleLocationId { get; }
    void SetForSaleLocationId(int? id);
}
