using System.IO;
using System.Text.Json;
using OmniCard.Models;

namespace OmniCard.Helpers;

/// <summary>
/// Shared implementation for per-game "last refreshed" timestamps persisted as a JSON map,
/// with a cooldown window. Backs <see cref="RefreshCooldownHelper"/> (bulk card data) and
/// <see cref="PriceRefreshCooldownHelper"/> (prices), which differ only in file name and window.
/// A corrupt or unreadable file is treated as "no record" rather than throwing.
/// </summary>
internal static class JsonPerGameCooldown
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static DateTime? GetLastRefresh(string dataDirectory, string fileName, CardGame game)
    {
        var path = Path.Combine(dataDirectory, fileName);
        if (!File.Exists(path))
            return null;

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

    public static void RecordRefresh(string dataDirectory, string fileName, CardGame game)
    {
        var path = Path.Combine(dataDirectory, fileName);
        Dictionary<string, DateTime> data;
        try
        {
            data = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(path)) ?? new()
                : new();
        }
        catch (Exception)
        {
            data = new();
        }

        data[game.ToString()] = DateTime.UtcNow;
        File.WriteAllText(path, JsonSerializer.Serialize(data, WriteOptions));
    }

    public static bool IsCooldownActive(
        string dataDirectory, string fileName, TimeSpan cooldown, CardGame game, out DateTime nextAvailable)
    {
        var last = GetLastRefresh(dataDirectory, fileName, game);
        if (last is null)
        {
            nextAvailable = default;
            return false;
        }

        nextAvailable = last.Value + cooldown;
        return DateTime.UtcNow < nextAvailable;
    }
}
