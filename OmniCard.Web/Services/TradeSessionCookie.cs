using System.Text.Json;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Services;

/// <summary>Tracks the user's <em>current</em> in-progress trade session across page navigations,
/// so the "Start a Trade" / "Add to trade session" buttons know whether a draft is already open and
/// which one. Stored as a cookie holding the draft's session id; validated against the on-disk
/// draft folder each read (a finalized/missing draft reads as "no active session").</summary>
public static class TradeSessionCookie
{
    public const string CookieName = "omnicard_trade";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>The current draft session id, or null if there is no live draft (never started,
    /// finalized, or the folder is gone).</summary>
    public static Guid? GetActive(HttpContext http, IDataPathService paths)
    {
        var raw = http.Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(raw) || !Guid.TryParse(raw, out var id))
            return null;

        var jsonPath = Path.Combine(paths.TradesDirectory, id.ToString(), "trade.json");
        if (!File.Exists(jsonPath))
            return null;
        try
        {
            var record = JsonSerializer.Deserialize<TradeSessionRecord>(File.ReadAllText(jsonPath), JsonOptions);
            return record is { SchemaVersion: >= 2 }
                   && string.Equals(record.Status, "draft", StringComparison.OrdinalIgnoreCase)
                ? id
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Set(HttpContext http, Guid id) =>
        http.Response.Cookies.Append(CookieName, id.ToString(), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(2),
        });

    public static void Clear(HttpContext http) => http.Response.Cookies.Delete(CookieName);
}
