using System.Text.Json;
using OmniCard.Shared.Kiosk;
using OmniCard.Shared.Settings;

namespace OmniCard.Web.Services.Kiosk;

/// <summary>File-backed <see cref="IKioskSettingsService"/>, persisting to <c>kiosk-settings.json</c> in the
/// data directory. Values are clamped/cleaned on both read and write.</summary>
public sealed class KioskSettingsService : IKioskSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly object _lock = new();

    public KioskSettingsService(IDataPathService dataPathService)
    {
        _filePath = Path.Combine(dataPathService.DataDirectory, "kiosk-settings.json");
    }

    public KioskSettings Get()
    {
        KioskSettings settings;
        lock (_lock)
        {
            try
            {
                settings = File.Exists(_filePath)
                    ? JsonSerializer.Deserialize<KioskSettings>(File.ReadAllText(_filePath), JsonOptions) ?? new()
                    : new KioskSettings();
            }
            catch (JsonException)
            {
                settings = new KioskSettings();
            }
        }
        Normalize(settings);
        return settings;
    }

    public void Save(KioskSettings settings)
    {
        Normalize(settings);
        lock (_lock)
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    internal static void Normalize(KioskSettings s)
    {
        s.IdleTimeoutMinutes = Math.Clamp(s.IdleTimeoutMinutes,
            KioskSettings.MinIdleTimeoutMinutes, KioskSettings.MaxIdleTimeoutMinutes);
        s.LocationIds = (s.LocationIds ?? []).Where(id => id > 0).Distinct().ToList();
        s.WelcomeMessage = string.IsNullOrWhiteSpace(s.WelcomeMessage)
            ? null
            : s.WelcomeMessage.Trim() is var m && m.Length > KioskSettings.MaxWelcomeMessageLength
                ? m[..KioskSettings.MaxWelcomeMessageLength]
                : s.WelcomeMessage.Trim();
    }
}
