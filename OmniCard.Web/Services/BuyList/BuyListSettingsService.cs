using System.Text.Json;
using OmniCard.Shared.BuyList;
using OmniCard.Shared.Settings;

namespace OmniCard.Web.Services.BuyList;

/// <summary>File-backed <see cref="IBuyListSettingsService"/>, persisting to <c>buy-list-settings.json</c> in
/// the data directory.</summary>
public sealed class BuyListSettingsService : IBuyListSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly object _lock = new();

    public BuyListSettingsService(IDataPathService dataPathService)
    {
        _filePath = Path.Combine(dataPathService.DataDirectory, "buy-list-settings.json");
    }

    public BuyListSettings Get()
    {
        lock (_lock)
        {
            try
            {
                return File.Exists(_filePath)
                    ? JsonSerializer.Deserialize<BuyListSettings>(File.ReadAllText(_filePath), JsonOptions) ?? new()
                    : new BuyListSettings();
            }
            catch (JsonException)
            {
                return new BuyListSettings();
            }
        }
    }

    public void Save(BuyListSettings settings)
    {
        lock (_lock)
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
