using OmniCard.Models;

namespace OmniCard.Helpers;

/// <summary>Per-game 24h throttle for full bulk card-data refreshes (Scryfall/OPTCG downloads).</summary>
public static class RefreshCooldownHelper
{
    private const string FileName = "refresh-timestamps.json";
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromHours(24);

    public static DateTime? GetLastRefresh(string dataDirectory, CardGame game)
        => JsonPerGameCooldown.GetLastRefresh(dataDirectory, FileName, game);

    public static void RecordRefresh(string dataDirectory, CardGame game)
        => JsonPerGameCooldown.RecordRefresh(dataDirectory, FileName, game);

    public static bool IsCooldownActive(string dataDirectory, CardGame game, out DateTime nextAvailable)
        => JsonPerGameCooldown.IsCooldownActive(dataDirectory, FileName, CooldownPeriod, game, out nextAvailable);
}
