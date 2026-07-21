using System.IO;
using System.Text.Json;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class SalesSettingsService : ISalesSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IDataPathService _dataPath;
    private readonly string _filePath;

    public SalesSettingsService(IDataPathService dataPathService)
    {
        _dataPath = dataPathService;
        _filePath = Path.Combine(dataPathService.DataDirectory, "sales-settings.json");
    }

    public int? ForSaleLocationId => Load().ForSaleLocationId;

    public void SetForSaleLocationId(int? id)
    {
        var settings = Load();
        settings.ForSaleLocationId = id;
        Save(settings);
    }

    public CompanyProfile GetCompany() => Load().Company;

    public void SaveCompany(CompanyProfile company)
    {
        var settings = Load();
        settings.Company = company;
        Save(settings);
    }

    public ReceiptSettings GetReceipt() => Load().Receipt;

    public void SaveReceipt(ReceiptSettings receipt)
    {
        var settings = Load();
        settings.Receipt = receipt;
        Save(settings);
    }

    public string SetLogo(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath);
        var destName = "company-logo" + ext;
        var dest = Path.Combine(_dataPath.DataDirectory, destName);
        File.Copy(sourcePath, dest, overwrite: true);
        return destName;
    }

    private SalesSettings Load()
    {
        SalesSettings settings;
        if (!File.Exists(_filePath))
            settings = new SalesSettings();
        else
        {
            try
            {
                settings = JsonSerializer.Deserialize<SalesSettings>(File.ReadAllText(_filePath), JsonOptions)
                           ?? new SalesSettings();
            }
            catch (JsonException)
            {
                settings = new SalesSettings();
            }
        }

        // Guard against old files (or explicit nulls) lacking the phase-3 sections.
        settings.Company ??= new CompanyProfile();
        settings.Receipt ??= new ReceiptSettings();
        return settings;
    }

    private void Save(SalesSettings settings)
        => File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
}
