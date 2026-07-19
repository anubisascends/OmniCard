using OmniCard.Models;

namespace OmniCard.Helpers;

/// <summary>Per-game 24h throttle for background price refreshes. Persists to its own file so
/// it is independent of the bulk-data refresh cooldown (<see cref="RefreshCooldownHelper"/>).</summary>
public static class PriceRefreshCooldownHelper
{
    private const string FileName = "price-refresh-timestamps.json";
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromHours(24);

    public static DateTime? GetLastRefresh(string dataDirectory, CardGame game)
        => JsonPerGameCooldown.GetLastRefresh(dataDirectory, FileName, game);

    public static void RecordRefresh(string dataDirectory, CardGame game)
        => JsonPerGameCooldown.RecordRefresh(dataDirectory, FileName, game);

    public static bool IsCooldownActive(string dataDirectory, CardGame game, out DateTime nextAvailable)
        => JsonPerGameCooldown.IsCooldownActive(dataDirectory, FileName, CooldownPeriod, game, out nextAvailable);
}
