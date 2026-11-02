using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Dashboard;

namespace OmniCard.Web.Services.Dashboard;

/// <summary>
/// Saved Dashboards (see <see cref="DashboardLayout"/>): each user's own, everyone's default (set by an
/// administrator) and the <see cref="BuiltIn"/> layout shown when neither exists — the classic totals
/// and breakdown tables, rebuilt from widgets so an upgrade shows the same Dashboard as before.
///
/// <para>Every layout is <see cref="Sanitize">sanitized</see> on the way in and out, so stored state is
/// always drawable whatever a client sent or an older version wrote.</para>
///
/// <para><b>SQL widgets.</b> Writing SQL needs the <c>dashboard.sql</c> permission. A user without it
/// may still keep — and see — the SQL widgets of everyone's default in their own layout, but only with
/// SQL that the default currently contains (<see cref="DefaultSql"/>): change the SQL, or the
/// administrator removes it from the default, and it no longer runs for them.</para>
/// </summary>
public sealed partial class DashboardLayoutService(IDbContextFactory<OmniCardDbContext> factory, TimeProvider time)
{
    public const string SourcePersonal = "personal";
    public const string SourceDefault = "default";
    public const string SourceBuiltIn = "builtIn";

    public const int MaxWidgets = 60;
    public const int MaxHeight = 40;
    public const int MaxTitleLength = 100;
    public const int MaxQueryLength = 2000;
    public const int MaxTextLength = 10_000;
    public const int MaxLimit = 100;
    private const int MaxColumnNameLength = 128;
    private const int MaxValueColumns = 20;

    public static readonly IReadOnlyList<string> Types = ["metric", "breakdown", "search", "sql", "note"];

    /// <summary>Totals a <c>metric</c> widget can show (see <see cref="DashboardDataService"/>).</summary>
    public static readonly IReadOnlyList<string> Metrics =
    [
        "totalUnits", "totalCost", "totalMarket", "unrealized",
        "realizedProfit", "realizedProceeds", "realizedCost", "realizedFees", "totalSold",
        "shippingCost", "shippingCharged",
    ];

    public static readonly IReadOnlyList<string> Breakdowns = ["game", "category", "salesGame"];
    public static readonly IReadOnlyList<string> SearchOutputs = ["summary", "cards", "groups"];
    public static readonly IReadOnlyList<string> CardSorts = ["value", "price", "quantity", "name", "added"];
    public static readonly IReadOnlyList<string> GroupSorts = ["value", "units", "cards", "group"];
    public static readonly IReadOnlyList<string> Displays = ["stat", "table", "chart"];
    public static readonly IReadOnlyList<string> Charts = ["bar", "horizontalBar", "line", "area", "pie"];
    public static readonly IReadOnlyList<string> Formats = ["number", "money", "percent", "text", "date"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The Dashboard as it looked before widgets: five totals over two breakdown tables.
    /// Titles are left blank so the page shows them in the reader's language.</summary>
    public static DashboardLayoutDto BuiltIn { get; } = Sanitize(new()
    {
        Widgets =
        [
            Metric("total-units", "totalUnits", 0),
            Metric("total-cost", "totalCost", 12),
            Metric("total-market", "totalMarket", 24),
            Metric("unrealized", "unrealized", 36, colorBySign: true),
            Metric("realized-profit", "realizedProfit", 48, colorBySign: true),
            new() { Id = "by-game", Type = "breakdown", GroupBy = "game", Display = "table", X = 0, Y = 3, W = 30, H = 8 },
            new() { Id = "by-category", Type = "breakdown", GroupBy = "category", Display = "table", X = 30, Y = 3, W = 30, H = 8 },
        ],
    });

    private static DashboardWidgetDto Metric(string id, string metric, int x, bool colorBySign = false) => new()
    {
        Id = id, Type = "metric", Metric = metric, Display = "stat", ColorBySign = colorBySign, X = x, Y = 0, W = 12, H = 3,
    };

    // --- reads --------------------------------------------------------------------------------------

    /// <summary>The layout <paramref name="userId"/> sees and where it came from (<see cref="SourcePersonal"/>,
    /// <see cref="SourceDefault"/> or <see cref="SourceBuiltIn"/>), plus everyone's default.</summary>
    public (DashboardLayoutDto Layout, string Source, DashboardLayoutDto Default, string DefaultSource) Resolve(int userId)
    {
        using var db = factory.CreateDbContext();
        var rows = db.DashboardLayouts.AsNoTracking()
            .Where(d => d.UserId == userId || d.UserId == null)
            .ToList();
        var personal = rows.FirstOrDefault(r => r.UserId == userId);
        var everyone = rows.FirstOrDefault(r => r.UserId is null);

        var (defaultLayout, defaultSource) = everyone is null
            ? (BuiltIn, SourceBuiltIn)
            : (Deserialize(everyone.LayoutJson), SourceDefault);
        return personal is null
            ? (defaultLayout, defaultSource, defaultLayout, defaultSource)
            : (Deserialize(personal.LayoutJson), SourcePersonal, defaultLayout, defaultSource);
    }

    /// <summary>Every SQL text on everyone's current default, normalized (see <see cref="NormalizeSql"/>).</summary>
    public IReadOnlySet<string> DefaultSql()
    {
        using var db = factory.CreateDbContext();
        var json = db.DashboardLayouts.AsNoTracking().Where(d => d.UserId == null).Select(d => d.LayoutJson).FirstOrDefault();
        return SqlOf(json is null ? BuiltIn : Deserialize(json));
    }

    /// <summary>The SQL texts in <paramref name="layout"/>'s SQL widgets, normalized.</summary>
    public static HashSet<string> SqlOf(DashboardLayoutDto layout) =>
        layout.Widgets
            .Where(w => w.Type == "sql" && !string.IsNullOrWhiteSpace(w.Sql))
            .Select(w => NormalizeSql(w.Sql!))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>SQL compared for "is this the default's query": trimmed, line endings unified.</summary>
    public static string NormalizeSql(string sql) => sql.Replace("\r\n", "\n").Trim();

    // --- writes -------------------------------------------------------------------------------------

    /// <summary>Save <paramref name="userId"/>'s own Dashboard. Without <paramref name="canWriteSql"/>,
    /// every SQL widget must carry SQL from everyone's default or from the user's saved layout.</summary>
    public DashboardLayoutDto SavePersonal(int userId, DashboardLayoutDto layout, bool canWriteSql)
    {
        var clean = Sanitize(layout);
        using var db = factory.CreateDbContext();
        var row = db.DashboardLayouts.FirstOrDefault(d => d.UserId == userId);
        if (!canWriteSql)
        {
            var allowed = new HashSet<string>(DefaultSql(), StringComparer.Ordinal);
            if (row is not null)
                allowed.UnionWith(SqlOf(Deserialize(row.LayoutJson)));
            RequireSqlFrom(clean, allowed);
        }
        RequireValidSql(clean);
        Upsert(db, row, userId, clean);
        return clean;
    }

    /// <summary>Save everyone's default (administrators — checked by the caller).</summary>
    public DashboardLayoutDto SaveDefault(DashboardLayoutDto layout)
    {
        var clean = Sanitize(layout);
        RequireValidSql(clean);
        using var db = factory.CreateDbContext();
        Upsert(db, db.DashboardLayouts.FirstOrDefault(d => d.UserId == null), null, clean);
        return clean;
    }

    /// <summary>Drop the user's own Dashboard, so they see everyone's default again.</summary>
    public void ResetPersonal(int userId)
    {
        using var db = factory.CreateDbContext();
        db.DashboardLayouts.Where(d => d.UserId == userId).ExecuteDelete();
    }

    /// <summary>Drop everyone's default, so users without their own see the built-in Dashboard.</summary>
    public void ResetDefault()
    {
        using var db = factory.CreateDbContext();
        db.DashboardLayouts.Where(d => d.UserId == null).ExecuteDelete();
    }

    private void Upsert(OmniCardDbContext db, DashboardLayout? row, int? userId, DashboardLayoutDto layout)
    {
        var json = JsonSerializer.Serialize(layout, JsonOptions);
        var now = time.GetUtcNow().UtcDateTime;
        if (row is null)
            db.DashboardLayouts.Add(new DashboardLayout { UserId = userId, LayoutJson = json, UpdatedAt = now });
        else
        {
            row.LayoutJson = json;
            row.UpdatedAt = now;
        }
        db.SaveChanges();
    }

    private static void RequireSqlFrom(DashboardLayoutDto layout, IReadOnlySet<string> allowed)
    {
        if (layout.Widgets.Any(w => w.Type == "sql" && !string.IsNullOrWhiteSpace(w.Sql) && !allowed.Contains(NormalizeSql(w.Sql!))))
            throw new DashboardException(DashboardErrorKind.Forbidden,
                "You don't have permission to write SQL widgets. You can keep the SQL widgets from the default dashboard, but not change their queries.");
    }

    private static void RequireValidSql(DashboardLayoutDto layout)
    {
        foreach (var w in layout.Widgets.Where(w => w.Type == "sql" && !string.IsNullOrWhiteSpace(w.Sql)))
            if (DashboardSqlValidator.Validate(w.Sql) is { } problem)
                throw new DashboardException(DashboardErrorKind.BadRequest,
                    $"{(string.IsNullOrWhiteSpace(w.Title) ? "A SQL widget" : $"\"{w.Title}\"")}: {problem}");
    }

    // --- sanitizing ---------------------------------------------------------------------------------

    private static DashboardLayoutDto Deserialize(string json)
    {
        try
        {
            return Sanitize(JsonSerializer.Deserialize<DashboardLayoutDto>(json, JsonOptions) ?? new DashboardLayoutDto());
        }
        catch (JsonException)
        {
            return new DashboardLayoutDto();
        }
    }

    /// <summary>
    /// Clamp a layout to what the page can draw: known widget types only (unknown ones are dropped),
    /// unique ids, positions inside the <see cref="DashboardLayoutDto.Columns"/>-column grid, known option
    /// values, bounded text. Fields a widget's type doesn't use are cleared.
    /// </summary>
    public static DashboardLayoutDto Sanitize(DashboardLayoutDto? layout)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var widgets = new List<DashboardWidgetDto>();
        foreach (var w in layout?.Widgets ?? [])
        {
            if (w is null || widgets.Count == MaxWidgets) continue;
            var type = OneOf(w.Type, Types);
            if (type is null) continue;

            var id = w.Id is { } raw && IdPattern().IsMatch(raw) && !ids.Contains(raw) ? raw : NewId(ids);
            ids.Add(id);

            var width = Math.Clamp(w.W <= 0 ? 12 : w.W, 1, DashboardLayoutDto.Columns);
            var isData = type != "note";
            var output = type == "search" ? OneOf(w.Output, SearchOutputs) ?? "summary" : null;
            widgets.Add(new DashboardWidgetDto
            {
                Id = id,
                Type = type,
                Title = Text(w.Title, MaxTitleLength),
                X = Math.Clamp(w.X, 0, DashboardLayoutDto.Columns - width),
                Y = Math.Clamp(w.Y, 0, 10_000),
                W = width,
                H = Math.Clamp(w.H <= 0 ? 4 : w.H, 1, MaxHeight),

                Metric = type == "metric" ? OneOf(w.Metric, Metrics) ?? "totalMarket" : null,
                GroupBy = type switch
                {
                    "breakdown" => OneOf(w.GroupBy, Breakdowns) ?? "game",
                    "search" when output == "groups" => CollectionGrouping.Canonicalize([w.GroupBy]).FirstOrDefault() ?? "setCode",
                    _ => null,
                },
                Game = type == "search" ? Game(w.Game) : null,
                Query = type == "search" ? Text(w.Query, MaxQueryLength) : null,
                Output = output,
                Sort = output switch
                {
                    "cards" => OneOf(w.Sort, CardSorts) ?? "value",
                    "groups" => OneOf(w.Sort, GroupSorts) ?? "value",
                    _ => null,
                },
                Limit = output is "cards" or "groups" ? Math.Clamp(w.Limit ?? 10, 1, MaxLimit) : null,
                Sql = type == "sql" ? Truncate(w.Sql?.Trim() ?? "", DashboardSqlValidator.MaxLength) : null,
                Database = type == "sql" ? Database(w.Database) : null,
                Text = type == "note" ? Truncate(w.Text ?? "", MaxTextLength) : null,

                Display = isData ? OneOf(w.Display, Displays) ?? DefaultDisplay(type, output) : null,
                Chart = isData ? OneOf(w.Chart, Charts) ?? "bar" : null,
                LabelColumn = isData ? Text(w.LabelColumn, MaxColumnNameLength) : null,
                ValueColumns = isData ? ColumnList(w.ValueColumns) : null,
                StatColumn = isData ? Text(w.StatColumn, MaxColumnNameLength) : null,
                Formats = isData ? FormatMap(w.Formats) : null,
                ColorBySign = isData && w.ColorBySign,
            });
        }
        return new DashboardLayoutDto { Version = 1, Widgets = widgets };
    }

    private static string DefaultDisplay(string type, string? output) => type switch
    {
        "metric" => "stat",
        "search" when output == "summary" => "stat",
        _ => "table",
    };

    private static string? OneOf(string? value, IReadOnlyList<string> allowed) =>
        allowed.FirstOrDefault(a => string.Equals(a, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? Game(string? game) =>
        Enum.TryParse<CardGame>(game, ignoreCase: true, out var g) && Enum.IsDefined(g) ? g.ToString() : null;

    private static string Database(string? database)
    {
        if (Game(database) is { } game) return game;
        return DashboardSqlRunner.MainDatabase;
    }

    private static string? Text(string? value, int max)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : Truncate(trimmed, max);
    }

    private static List<string>? ColumnList(List<string>? columns)
    {
        var list = (columns ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c) && c.Length <= MaxColumnNameLength)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxValueColumns)
            .ToList();
        return list.Count == 0 ? null : list;
    }

    private static Dictionary<string, string>? FormatMap(Dictionary<string, string>? formats)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (column, format) in formats ?? [])
            if (!string.IsNullOrWhiteSpace(column) && column.Length <= MaxColumnNameLength && OneOf(format, Formats) is { } f
                && map.Count < 50)
                map[column] = f;
        return map.Count == 0 ? null : map;
    }

    private static string NewId(HashSet<string> taken)
    {
        string id;
        do id = "w" + Guid.NewGuid().ToString("N")[..10];
        while (taken.Contains(id));
        return id;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex IdPattern();
}
