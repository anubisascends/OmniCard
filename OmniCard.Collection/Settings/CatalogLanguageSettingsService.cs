using System.Text.Json;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Settings;

namespace OmniCard.Collection.Settings;

/// <summary>
/// File-backed <see cref="ICatalogLanguageSettingsService"/>, persisting to
/// <c>catalog-languages.json</c> in the data directory (mirrors <see cref="ScanBadgeSettingsService"/>).
/// The file maps game name → language codes; every read is re-sanitized so a hand-edited or stale
/// file can never select a language the game's source can't download.
/// </summary>
public sealed class CatalogLanguageSettingsService : ICatalogLanguageSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly object _lock = new();

    public CatalogLanguageSettingsService(IDataPathService dataPathService)
    {
        _filePath = Path.Combine(dataPathService.DataDirectory, "catalog-languages.json");
    }

    public IReadOnlyList<string> GetLanguages(CardGame game)
    {
        lock (_lock)
        {
            var stored = Load().GetValueOrDefault(game.ToString());
            return CardLanguages.SanitizeCatalogSelection(game, stored);
        }
    }

    public IReadOnlyList<string> SetLanguages(CardGame game, IEnumerable<string> languages)
    {
        var sanitized = CardLanguages.SanitizeCatalogSelection(game, languages);
        lock (_lock)
        {
            var all = Load();
            all[game.ToString()] = sanitized.ToList();
            File.WriteAllText(_filePath, JsonSerializer.Serialize(all, JsonOptions));
        }
        return sanitized;
    }

    private Dictionary<string, List<string>> Load()
    {
        if (!File.Exists(_filePath))
            return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_filePath), JsonOptions);
            return parsed is null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
