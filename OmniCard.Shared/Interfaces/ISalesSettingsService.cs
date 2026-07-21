using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface ISalesSettingsService
{
    int? ForSaleLocationId { get; }
    void SetForSaleLocationId(int? id);

    CompanyProfile GetCompany();
    void SaveCompany(CompanyProfile company);
    ReceiptSettings GetReceipt();
    void SaveReceipt(ReceiptSettings receipt);

    /// <summary>Copies the chosen image into the data directory and returns the stored
    /// path relative to the data directory (does not persist it — the caller assigns it
    /// to <see cref="CompanyProfile.LogoPath"/> and saves).</summary>
    string SetLogo(string sourcePath);
}
