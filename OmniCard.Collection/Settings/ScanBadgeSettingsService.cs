using System.Text.Json;
using OmniCard.Shared.Settings;

namespace OmniCard.Collection.Settings;

/// <summary>
/// File-backed <see cref="IScanBadgeSettingsService"/>, persisting to
/// <c>scan-badge-settings.json</c> in the data directory (mirrors <c>SalesSettingsService</c>).
/// </summary>
public sealed class ScanBadgeSettingsService : IScanBadgeSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    public ScanBadgeSettingsService(IDataPathService dataPathService)
    {
        _filePath = Path.Combine(dataPathService.DataDirectory, "scan-badge-settings.json");
    }

    public ScanBadgeSettings Get()
    {
        ScanBadgeSettings settings;
        if (!File.Exists(_filePath))
            settings = new ScanBadgeSettings();
        else
        {
            try
            {
                settings = JsonSerializer.Deserialize<ScanBadgeSettings>(File.ReadAllText(_filePath), JsonOptions)
                           ?? new ScanBadgeSettings();
            }
            catch (JsonException)
            {
                settings = new ScanBadgeSettings();
            }
        }

        // Guard against old/corrupt files: an empty or malformed threshold list falls back to defaults
        // so the scan page always has a usable ascending ladder.
        if (string.IsNullOrWhiteSpace(settings.CurrencyCode))
            settings.CurrencyCode = ScanBadgeSettings.DefaultCurrencyCode;
        settings.Thresholds = Sanitize(settings.Thresholds);
        return settings;
    }

    public void Save(string currencyCode, IEnumerable<decimal> thresholds)
    {
        var settings = new ScanBadgeSettings
        {
            CurrencyCode = string.IsNullOrWhiteSpace(currencyCode)
                ? ScanBadgeSettings.DefaultCurrencyCode
                : currencyCode.Trim().ToUpperInvariant(),
            Thresholds = Sanitize(thresholds),
        };
        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    /// <summary>Coerce arbitrary input into a valid ladder: keep positives, sort ascending, then pad
    /// (from the defaults) or truncate to exactly <see cref="ScanBadgeSettings.ThresholdCount"/>.</summary>
    private static List<decimal> Sanitize(IEnumerable<decimal>? thresholds)
    {
        var cleaned = (thresholds ?? [])
            .Where(t => t > 0)
            .OrderBy(t => t)
            .Take(ScanBadgeSettings.ThresholdCount)
            .ToList();

        // Pad any missing slots from the defaults, keeping the ladder strictly usable.
        for (var i = cleaned.Count; i < ScanBadgeSettings.ThresholdCount; i++)
            cleaned.Add(ScanBadgeSettings.DefaultThresholds[i]);

        return cleaned;
    }
}
