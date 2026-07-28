using System.IO;
using System.Text.Json;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class EbaySellingSettingsService : IEbaySellingSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    public EbaySellingSettingsService(IDataPathService dataPathService)
        => _filePath = Path.Combine(dataPathService.DataDirectory, "ebay-selling.json");

    public EbaySellingSettings Get()
    {
        if (!File.Exists(_filePath))
            return new EbaySellingSettings();
        try
        {
            return JsonSerializer.Deserialize<EbaySellingSettings>(File.ReadAllText(_filePath), JsonOptions)
                   ?? new EbaySellingSettings();
        }
        catch (JsonException)
        {
            return new EbaySellingSettings();
        }
    }

    public void Save(EbaySellingSettings settings)
        => File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));

    public bool IsSetupComplete()
    {
        var s = Get();
        return s.LocationProvisioned
            && !string.IsNullOrEmpty(s.FulfillmentPolicyId)
            && !string.IsNullOrEmpty(s.ReturnPolicyId);
    }
}
