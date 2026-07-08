using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Services;

public class CollectionPresetService : ICollectionPresetService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;

    public CollectionPresetService(IDataPathService dataPathService)
    {
        _filePath = Path.Combine(dataPathService.DataDirectory, "collection-presets.json");
    }

    public List<SortPreset> GetSortPresets(CardGame game)
    {
        var config = Load();
        return config.SortPresets.Where(p => p.Game == game).ToList();
    }

    public void SaveSortPreset(SortPreset preset)
    {
        var config = Load();
        config.SortPresets.RemoveAll(p => p.Name == preset.Name && p.Game == preset.Game);
        config.SortPresets.Add(preset);
        Save(config);
    }

    public void DeleteSortPreset(string name, CardGame game)
    {
        var config = Load();
        config.SortPresets.RemoveAll(p => p.Name == name && p.Game == game);

        // Clear active if it was the deleted preset
        var key = game.ToString();
        if (config.ActivePresets.TryGetValue(key, out var active) && active.SortPreset == name)
            active.SortPreset = null;

        Save(config);
    }

    public List<FilterPreset> GetFilterPresets(CardGame game)
    {
        var config = Load();
        return config.FilterPresets.Where(p => p.Game == game).ToList();
    }

    public void SaveFilterPreset(FilterPreset preset)
    {
        var config = Load();
        config.FilterPresets.RemoveAll(p => p.Name == preset.Name && p.Game == preset.Game);
        config.FilterPresets.Add(preset);
        Save(config);
    }

    public void DeleteFilterPreset(string name, CardGame game)
    {
        var config = Load();
        config.FilterPresets.RemoveAll(p => p.Name == name && p.Game == game);

        var key = game.ToString();
        if (config.ActivePresets.TryGetValue(key, out var active) && active.FilterPreset == name)
            active.FilterPreset = null;

        Save(config);
    }

    public void SetActiveSortPreset(CardGame game, string? name)
    {
        var config = Load();
        var key = game.ToString();
        if (!config.ActivePresets.TryGetValue(key, out var active))
        {
            active = new ActivePresetPair();
            config.ActivePresets[key] = active;
        }
        active.SortPreset = name;
        Save(config);
    }

    public void SetActiveFilterPreset(CardGame game, string? name)
    {
        var config = Load();
        var key = game.ToString();
        if (!config.ActivePresets.TryGetValue(key, out var active))
        {
            active = new ActivePresetPair();
            config.ActivePresets[key] = active;
        }
        active.FilterPreset = name;
        Save(config);
    }

    public SortPreset? GetActiveSortPreset(CardGame game)
    {
        var config = Load();
        var key = game.ToString();
        if (!config.ActivePresets.TryGetValue(key, out var active) || active.SortPreset is null)
            return null;

        return config.SortPresets.FirstOrDefault(p => p.Name == active.SortPreset && p.Game == game);
    }

    public FilterPreset? GetActiveFilterPreset(CardGame game)
    {
        var config = Load();
        var key = game.ToString();
        if (!config.ActivePresets.TryGetValue(key, out var active) || active.FilterPreset is null)
            return null;

        return config.FilterPresets.FirstOrDefault(p => p.Name == active.FilterPreset && p.Game == game);
    }

    private CollectionPresetConfig Load()
    {
        if (!File.Exists(_filePath))
            return new CollectionPresetConfig();

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<CollectionPresetConfig>(json, JsonOptions)
               ?? new CollectionPresetConfig();
    }

    private void Save(CollectionPresetConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
