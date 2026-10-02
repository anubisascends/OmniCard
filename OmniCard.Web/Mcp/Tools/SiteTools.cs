using System.ComponentModel;
using ModelContextProtocol.Server;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Sites;
using OmniCard.Web.Api.Controllers;
using OmniCard.Web.Services;

namespace OmniCard.Web.Mcp.Tools;

/// <summary>
/// Read-only MCP tools over <see cref="Site"/>s. A site is a MAJOR physical location — a home, a shop,
/// a storage unit — that holds many child storage locations (binders, boxes, deck boxes). The hierarchy
/// is Site ▸ Location ▸ Card. Every location belongs to exactly one site; the "Default" site holds
/// everything not explicitly placed elsewhere (including the system Bulk location).
///
/// <para>The MCP endpoint has no per-user identity (loopback-gated, or an external-IdP token that isn't
/// an OmniCard account), so these tools see every site — the per-user site visibility rules apply to
/// the SPA/API only.</para>
/// </summary>
[McpServerToolType]
public sealed class SiteTools(SiteService sites)
{
    [McpServerTool(Name = "list_sites")]
    [Description("List sites. A site is a MAJOR physical location (a home, a shop, a storage unit) that " +
        "contains many storage locations (binders, boxes, deck boxes); the hierarchy is Site > Location > Card. " +
        "Use a site's id or name as the 'site' filter of list_locations and search_collection to scope to " +
        "one physical place (e.g. 'what cards are at Mom's house?'). The Default site always exists and holds " +
        "every location not assigned elsewhere, including Bulk.")]
    public IReadOnlyList<SiteDto> ListSites()
    {
        var counts = sites.LocationCounts();
        return sites.GetAll()
            .Select(s => SitesController.ToDto(s, SiteAccessLevel.Write, counts.GetValueOrDefault(s.Id)))
            .ToList();
    }
}

/// <summary>Resolves the MCP tools' optional <c>site</c> argument (an id or a case-insensitive name).</summary>
internal static class McpSiteParsing
{
    /// <summary>Null/blank → null (all sites). Otherwise the matching site id, or a client-visible error
    /// listing the valid sites.</summary>
    public static int? ResolveSite(SiteService sites, string? site)
    {
        if (string.IsNullOrWhiteSpace(site))
            return null;
        var all = sites.GetAll();
        var trimmed = site.Trim();
        var match = int.TryParse(trimmed, out var id)
            ? all.FirstOrDefault(s => s.Id == id)
            : all.FirstOrDefault(s => string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        return match?.Id ?? throw new ArgumentException(
            $"Unknown site '{site}'. Use list_sites; valid sites: {string.Join(", ", all.Select(s => $"{s.Id} ({s.Name})"))}.");
    }
}
