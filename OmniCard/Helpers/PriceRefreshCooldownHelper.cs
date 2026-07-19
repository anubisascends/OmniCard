using System.IO;
using System.Text.Json;
using OmniCard.Models;

namespace OmniCard.Helpers;

/// <summary>Per-game 24h throttle for background price refreshes. Persists to its own file so
/// it is independent of the bulk-data refresh cooldown (RefreshCooldownHelper).</summary>
public static class PriceRefreshCooldownHelper
{
    private const string FileName = "price-refresh-timestamps.json";
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromHours(24);

    public static DateTime? GetLastRefresh(string dataDirectory, CardGame game)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path)) return null;
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(path));
            return data?.TryGetValue(game.ToString(), out var ts) == true ? ts : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void RecordRefresh(string dataDirectory, CardGame game)
    {
        var path = Path.Combine(dataDirectory, FileName);
        Dictionary<string, DateTime> data;
        try
        {
            data = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(path)) ?? new()
                : new();
        }
        catch (JsonException)
        {
            data = new();
        }

        data[game.ToString()] = DateTime.UtcNow;
        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool IsCooldownActive(string dataDirectory, CardGame game, out DateTime nextAvailable)
    {
        var last = GetLastRefresh(dataDirectory, game);
        if (last is null)
        {
            nextAvailable = default;
            return false;
        }

        nextAvailable = last.Value + CooldownPeriod;
        return DateTime.UtcNow < nextAvailable;
    }
}
