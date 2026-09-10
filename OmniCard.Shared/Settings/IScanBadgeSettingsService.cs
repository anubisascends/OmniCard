namespace OmniCard.Shared.Settings;

/// <summary>Reads and persists the scan page's value-tier badge configuration
/// (<see cref="ScanBadgeSettings"/>).</summary>
public interface IScanBadgeSettingsService
{
    /// <summary>The current settings, falling back to sane defaults when nothing has been saved yet
    /// (never null; <see cref="ScanBadgeSettings.Thresholds"/> is always a non-empty ascending list).</summary>
    ScanBadgeSettings Get();

    /// <summary>Persists new settings. The currency code is normalized to upper-case and the thresholds
    /// are sanitized (positive, sorted ascending, padded/truncated to
    /// <see cref="ScanBadgeSettings.ThresholdCount"/>) before saving.</summary>
    void Save(string currencyCode, IEnumerable<decimal> thresholds);
}
