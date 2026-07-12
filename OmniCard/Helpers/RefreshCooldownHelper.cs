using System.IO;
using System.Text.Json;
using OmniCard.Models;

namespace OmniCard.Helpers;

public static class RefreshCooldownHelper
{
    private const string FileName = "refresh-timestamps.json";
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromHours(24);

    public static DateTime? GetLastRefresh(string dataDirectory, CardGame game)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);
            return data?.TryGetValue(game.ToString(), out var ts) == true ? ts : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void RecordRefresh(string dataDirectory, CardGame game)
    {
        var path = Path.Combine(dataDirectory, FileName);
        Dictionary<string, DateTime> data;

        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                data = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json) ?? new();
            }
            catch (JsonException)
            {
                data = new();
            }
        }
        else
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
