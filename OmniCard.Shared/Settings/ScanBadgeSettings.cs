namespace OmniCard.Shared.Settings;

/// <summary>
/// Persisted configuration for the scan page's value-tier "currency sign" badges. Four ascending
/// price ceilings split matched cards into five value tiers (one to five currency signs); the fifth
/// tier is "priced above the last threshold". <see cref="CurrencyCode"/> is the ISO 4217 code the
/// thresholds — and the catalog prices they're compared against — are denominated in (default USD).
/// </summary>
public sealed class ScanBadgeSettings
{
    /// <summary>The default tier ceilings (USD): ≤$1, ≤$5, ≤$20, ≤$50, then &gt;$50.</summary>
    public static readonly IReadOnlyList<decimal> DefaultThresholds = [1m, 5m, 20m, 50m];

    public const string DefaultCurrencyCode = "USD";

    /// <summary>Number of finite thresholds (one fewer than the number of tiers).</summary>
    public const int ThresholdCount = 4;

    public string CurrencyCode { get; set; } = DefaultCurrencyCode;

    public List<decimal> Thresholds { get; set; } = [.. DefaultThresholds];
}
