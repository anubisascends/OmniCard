using System.IO;
using System.Text.Json;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class SalesSettingsService : ISalesSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    public SalesSettingsService(IDataPathService dataPathService)
    {
        _filePath = Path.Combine(dataPathService.DataDirectory, "sales-settings.json");
    }

    public int? ForSaleLocationId => Load().ForSaleLocationId;

    public void SetForSaleLocationId(int? id)
    {
        var settings = Load();
        settings.ForSaleLocationId = id;
        Save(settings);
    }

    private SalesSettings Load()
    {
        if (!File.Exists(_filePath))
            return new SalesSettings();
        try
        {
            return JsonSerializer.Deserialize<SalesSettings>(File.ReadAllText(_filePath), JsonOptions) ?? new SalesSettings();
        }
        catch (System.Text.Json.JsonException)
        {
            return new SalesSettings();
        }
    }

    private void Save(SalesSettings settings)
        => File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
}
