using System.Text.Json;
using System.Text.Json.Serialization;
using OmniCard.Shared.Intake;
using OmniCard.Shared.Settings;

namespace OmniCard.Web.Services.Intake;

/// <summary>File-backed <see cref="IIntakeSettingsService"/>, persisting to <c>intake-settings.json</c> in the
/// data directory. Values are clamped/cleaned on both read and write.</summary>
public sealed class IntakeSettingsService : IIntakeSettingsService
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly object _lock = new();

    public IntakeSettingsService(IDataPathService dataPathService)
    {
        _filePath = Path.Combine(dataPathService.DataDirectory, "intake-settings.json");
    }

    public IntakeSettings Get()
    {
        IntakeSettings settings;
        lock (_lock)
        {
            try
            {
                settings = File.Exists(_filePath)
                    ? JsonSerializer.Deserialize<IntakeSettings>(File.ReadAllText(_filePath), JsonOptions) ?? new()
                    : new IntakeSettings();
            }
            catch (JsonException)
            {
                settings = new IntakeSettings();
            }
        }
        Normalize(settings);
        return settings;
    }

    public void Save(IntakeSettings settings)
    {
        Normalize(settings);
        lock (_lock)
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    internal static void Normalize(IntakeSettings s)
    {
        s.CashPercent = Percent(s.CashPercent);
        s.TradePercent = Percent(s.TradePercent);

        // Exactly the known conditions, each 0–100 (a missing one pays full price).
        var given = s.ConditionPercents ?? [];
        s.ConditionPercents = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var condition in IntakeSettings.Conditions)
            s.ConditionPercents[condition] = given.TryGetValue(condition, out var pct)
                ? Math.Round(Math.Clamp(pct, 0m, 100m), 2)
                : 100m;

        s.BulkThreshold = Money(s.BulkThreshold);
        s.BulkRate = Money(s.BulkRate);
        if (!Enum.IsDefined(s.BulkMode)) s.BulkMode = IntakeBulkMode.FlatRate;
        // Snap to an offered step (anything else would be a surprise on the counter).
        s.RoundingStep = IntakeSettings.RoundingSteps.Contains(s.RoundingStep) ? s.RoundingStep : 0m;
        if (!Enum.IsDefined(s.RoundingScope)) s.RoundingScope = IntakeRoundingScope.Total;
        if (s.LocationId is <= 0) s.LocationId = null;
        s.CustomerView ??= new IntakeCustomerView();
    }

    private static decimal Percent(decimal value) => Math.Round(Math.Clamp(value, 0m, IntakeSettings.MaxPercent), 2);

    private static decimal Money(decimal value) => Math.Round(Math.Max(0m, value), 2);
}
