using System.Text.Json;
using System.Text.Json.Serialization;
using OmniCard.Shared.Settings;

namespace OmniCard.Collection.Settings;

/// <summary>
/// File-backed <see cref="ICatalogScheduleSettingsService"/>: the schedule persists to
/// <c>catalog-schedule.json</c> and the last run's outcome to <c>catalog-schedule-state.json</c>, both in
/// the data directory (mirrors <see cref="ScanFolderSettingsService"/>).
/// </summary>
public sealed class CatalogScheduleSettingsService : ICatalogScheduleSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _settingsPath;
    private readonly string _statePath;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();

    public event Action? Changed;

    public CatalogScheduleSettingsService(IDataPathService dataPathService, TimeProvider? clock = null)
    {
        _settingsPath = Path.Combine(dataPathService.DataDirectory, "catalog-schedule.json");
        _statePath = Path.Combine(dataPathService.DataDirectory, "catalog-schedule-state.json");
        _clock = clock ?? TimeProvider.System;
    }

    public CatalogScheduleSettings Get()
    {
        var settings = Read<CatalogScheduleSettings>(_settingsPath) ?? new CatalogScheduleSettings();
        Normalize(settings);
        return settings;
    }

    public IReadOnlyList<string> Save(CatalogScheduleSettings settings)
    {
        Normalize(settings);
        var errors = Validate(settings);
        if (errors.Count > 0)
            return errors;

        settings.SavedAtUtc = _clock.GetUtcNow().UtcDateTime;
        Write(_settingsPath, settings);
        Changed?.Invoke();
        return [];
    }

    public CatalogScheduleRunState GetRunState() => Read<CatalogScheduleRunState>(_statePath) ?? new CatalogScheduleRunState();

    public void SaveRunState(CatalogScheduleRunState state) => Write(_statePath, state);

    /// <summary>Canonical "HH:mm" times (sorted, deduplicated; unparseable entries are kept so Validate
    /// can report them), distinct sorted days and games.</summary>
    internal static void Normalize(CatalogScheduleSettings s)
    {
        s.Times = (s.Times ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => CatalogScheduleSettings.TryParseTime(t, out var time) ? time.ToString("HH:mm") : t.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        s.Days = (s.Days ?? []).Distinct().Order().ToList();
        s.Games = (s.Games ?? []).Distinct().Order().ToList();
    }

    internal static List<string> Validate(CatalogScheduleSettings s)
    {
        var errors = s.Times
            .Where(t => !CatalogScheduleSettings.TryParseTime(t, out _))
            .Select(t => $"'{t}' isn't a valid time (use 24-hour HH:mm, e.g. 06:30).")
            .ToList();
        if (s.Times.Count > CatalogScheduleSettings.MaxTimes)
            errors.Add($"At most {CatalogScheduleSettings.MaxTimes} run times are allowed.");
        if (s.Enabled)
        {
            if (s.Times.Count == 0) errors.Add("Add at least one run time.");
            if (s.Days.Count == 0) errors.Add("Pick at least one day.");
            if (s.Games.Count == 0) errors.Add("Pick at least one game.");
            if (!s.UpdateCards && !s.UpdatePrices) errors.Add("Pick card data, prices, or both.");
        }
        return errors;
    }

    private T? Read<T>(string path) where T : class
    {
        lock (_lock)
        {
            if (!File.Exists(path)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private void Write<T>(string path, T value)
    {
        lock (_lock)
            File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }
}
