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
/// <para>
/// Languages a catalog source was found to serve beyond the built-in list (Scryfall adding a new
/// one-off language) live in <c>catalog-languages-discovered.json</c> (same shape). They are registered
/// with <see cref="CardLanguages.RegisterDiscovered"/> when this service is created — the web host
/// resolves it at startup — so they survive a restart before the next refresh re-finds them.
/// </para>
/// </summary>
public sealed class CatalogLanguageSettingsService : ICatalogLanguageSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly string _discoveredPath;
    private readonly object _lock = new();

    public CatalogLanguageSettingsService(IDataPathService dataPathService)
    {
        _filePath = Path.Combine(dataPathService.DataDirectory, "catalog-languages.json");
        _discoveredPath = Path.Combine(dataPathService.DataDirectory, "catalog-languages-discovered.json");
        // Register before anything reads the selection, so a discovered language that was ticked
        // survives sanitizing.
        foreach (var (gameName, codes) in Load(_discoveredPath))
            if (Enum.TryParse<CardGame>(gameName, ignoreCase: true, out var game))
                CardLanguages.RegisterDiscovered(game, codes);
    }

    public IReadOnlyList<string> GetLanguages(CardGame game)
    {
        lock (_lock)
        {
            var stored = Load(_filePath).GetValueOrDefault(game.ToString());
            return CardLanguages.SanitizeCatalogSelection(game, stored);
        }
    }

    public IReadOnlyList<string> SetLanguages(CardGame game, IEnumerable<string> languages)
    {
        var sanitized = CardLanguages.SanitizeCatalogSelection(game, languages);
        lock (_lock)
        {
            var all = Load(_filePath);
            all[game.ToString()] = sanitized.ToList();
            File.WriteAllText(_filePath, JsonSerializer.Serialize(all, JsonOptions));
        }
        return sanitized;
    }

    public IReadOnlyList<string> AddDiscoveredLanguages(CardGame game, IEnumerable<string> languages)
    {
        lock (_lock)
        {
            var added = CardLanguages.RegisterDiscovered(game, languages);
            if (added.Count == 0) return added;

            var all = Load(_discoveredPath);
            var codes = all.GetValueOrDefault(game.ToString()) ?? [];
            codes.AddRange(added.Where(c => !codes.Contains(c)));
            all[game.ToString()] = codes;
            File.WriteAllText(_discoveredPath, JsonSerializer.Serialize(all, JsonOptions));
            return added;
        }
    }

    private static Dictionary<string, List<string>> Load(string path)
    {
        if (!File.Exists(path))
            return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(path), JsonOptions);
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
