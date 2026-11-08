namespace OmniCard.Shared.Binder;

/// <summary>
/// A named divider in a binder: a bookmark that marks where a section starts (e.g. "Borderless",
/// "Showcase · Blue"). It sits just before <see cref="Page"/>, so its section runs from that page up to
/// the next divider. It belongs to the binder's pages, not to any card: it never moves or changes a
/// card. Page edits carry it along — inserting, moving or removing sheets renumbers it like the cards on
/// its page (see <see cref="Remap"/>), and shifting cards carries it with the cards on its page.
/// </summary>
public class BinderDivider
{
    public int Id { get; set; }
    public int ContainerId { get; set; }

    /// <summary>The 1-based page the divider sits in front of (its section's first page).</summary>
    public int Page { get; set; }

    public string Name { get; set; } = "";

    /// <summary>One of <see cref="Colors"/>, or null for the default tab color.</summary>
    public string? Color { get; set; }

    public const int MaxNameLength = 60;
    public const int MaxPerBinder = 100;

    /// <summary>The tab colors a divider can have (the SPA maps each to a swatch).</summary>
    public static readonly IReadOnlyList<string> Colors =
        ["red", "orange", "yellow", "green", "teal", "blue", "purple", "pink", "brown", "gray"];

    /// <summary>A known color key (case-insensitive), or null for anything else.</summary>
    public static string? NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        var key = color.Trim().ToLowerInvariant();
        return Colors.Contains(key) ? key : null;
    }

    /// <summary>
    /// Where a divider on <paramref name="page"/> goes after a sheet transform's old-page → new-page
    /// <paramref name="remap"/> (see <see cref="BinderSheetLayout"/>). An unmapped page keeps its number.
    /// A removed page (null target) passes the divider to the first surviving page after it — the
    /// section now starts there — falling back to the last page when nothing follows.
    /// </summary>
    public static int Remap(int page, IReadOnlyDictionary<int, int?> remap, int newTotalPages)
    {
        if (!remap.TryGetValue(page, out var target))
            return Math.Clamp(page, 1, Math.Max(1, newTotalPages));
        if (target is int moved)
            return moved;

        // Removed: the next page that survives (pages after a removed sheet always map somewhere).
        var after = remap.Where(kv => kv.Key > page && kv.Value is not null).OrderBy(kv => kv.Key).FirstOrDefault();
        return after.Value is int next ? next : Math.Max(1, newTotalPages);
    }
}
