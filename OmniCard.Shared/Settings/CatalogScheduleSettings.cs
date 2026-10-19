using System.Globalization;
using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Settings;

/// <summary>
/// The automatic catalog update schedule (Settings ▸ Catalog data, admin only). At each of
/// <see cref="Times"/> (server local time) on each of <see cref="Days"/>, every game in
/// <see cref="Games"/> gets its card data (new sets/printings) and/or its prices refreshed — the same
/// jobs as the manual "Download catalog" / "Update prices" buttons. TCGCSV, the source for every game but
/// MTG (Scryfall), republishes daily, so one run a day keeps catalogs current.
/// </summary>
public sealed class CatalogScheduleSettings
{
    public bool Enabled { get; set; }

    /// <summary>Times of day as "HH:mm" (24-hour, server local time), sorted, no duplicates.</summary>
    public List<string> Times { get; set; } = ["06:00"];

    public List<DayOfWeek> Days { get; set; } = [.. Enum.GetValues<DayOfWeek>()];

    public List<CardGame> Games { get; set; } = [.. Enum.GetValues<CardGame>()];

    /// <summary>Run each game's card-data download (new sets and printings; new cards are hashed).</summary>
    public bool UpdateCards { get; set; } = true;

    /// <summary>Run each game's price update.</summary>
    public bool UpdatePrices { get; set; } = true;

    /// <summary>When the schedule was last saved (UTC). Slots before it never count as missed, so
    /// turning the schedule on doesn't immediately run a slot from earlier today.</summary>
    public DateTime? SavedAtUtc { get; set; }

    public const int MaxTimes = 24;

    /// <summary>Parses "HH:mm" (or "H:mm").</summary>
    public static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value?.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>The scheduled slots (UTC) between <paramref name="fromUtc"/> (exclusive) and
    /// <paramref name="toUtc"/> (inclusive), oldest first.</summary>
    public IEnumerable<DateTime> SlotsBetween(DateTime fromUtc, DateTime toUtc, TimeZoneInfo zone)
    {
        var times = Times.Select(t => TryParseTime(t, out var time) ? time : (TimeOnly?)null)
            .OfType<TimeOnly>().Distinct().Order().ToList();
        if (times.Count == 0 || Days.Count == 0 || toUtc <= fromUtc)
            yield break;

        // Walk local calendar days, one before → one after the window to cover offsets.
        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(fromUtc, zone)).AddDays(-1);
        var lastDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(toUtc, zone)).AddDays(1);
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            if (!Days.Contains(day.DayOfWeek)) continue;
            foreach (var time in times)
            {
                var slot = ToUtc(day.ToDateTime(time), zone);
                if (slot > fromUtc && slot <= toUtc)
                    yield return slot;
            }
        }
    }

    /// <summary>The next slot after <paramref name="nowUtc"/> within two weeks, or null when none.</summary>
    public DateTime? NextSlotAfter(DateTime nowUtc, TimeZoneInfo zone) =>
        SlotsBetween(nowUtc, nowUtc.AddDays(14), zone).Cast<DateTime?>().FirstOrDefault();

    /// <summary>The latest slot that is due at <paramref name="nowUtc"/>: after <paramref name="lastRunUtc"/>
    /// and <see cref="SavedAtUtc"/>, and at most a week old (a server that was off for longer catches up
    /// with a single run, not one per missed slot). Null when nothing is due or the schedule is off.</summary>
    public DateTime? DueSlot(DateTime nowUtc, DateTime? lastRunUtc, TimeZoneInfo zone)
    {
        if (!Enabled) return null;
        var from = new[] { lastRunUtc, SavedAtUtc, nowUtc.AddDays(-7) }.Max()!.Value;
        return SlotsBetween(from, nowUtc, zone).Cast<DateTime?>().LastOrDefault();
    }

    // A local time skipped by a DST change runs an hour later; an ambiguous one runs at its first occurrence.
    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
            local = local.AddHours(1);
        if (zone.IsAmbiguousTime(local))
            return local - zone.GetAmbiguousTimeOffsets(local).Max();
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}

/// <summary>The outcome of the last scheduled catalog update run (persisted beside the settings).</summary>
public sealed class CatalogScheduleRunState
{
    public DateTime? LastRunStartedUtc { get; set; }
    public DateTime? LastRunFinishedUtc { get; set; }

    /// <summary>Jobs that ran in the last run, and how many of them failed.</summary>
    public int JobCount { get; set; }
    public int FailedCount { get; set; }

    /// <summary>"Game operation: message" for each failed job of the last run.</summary>
    public List<string> Failures { get; set; } = [];
}

/// <summary>Reads and persists <see cref="CatalogScheduleSettings"/> and the last run's
/// <see cref="CatalogScheduleRunState"/>.</summary>
public interface ICatalogScheduleSettingsService
{
    /// <summary>The current settings (never null), normalized.</summary>
    CatalogScheduleSettings Get();

    /// <summary>Validates and persists new settings (stamping <see cref="CatalogScheduleSettings.SavedAtUtc"/>).
    /// Returns the validation errors; nothing is saved unless the list is empty.</summary>
    IReadOnlyList<string> Save(CatalogScheduleSettings settings);

    CatalogScheduleRunState GetRunState();

    void SaveRunState(CatalogScheduleRunState state);

    /// <summary>Raised after settings are saved, so the scheduler re-evaluates promptly.</summary>
    event Action? Changed;
}
