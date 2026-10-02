using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Sites;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// Sites — MAJOR physical locations (a home, a shop) that each hold many storage locations.
/// <c>GET</c> lists the sites the signed-in user can see (default site first, always present) with
/// their access level; it backs the Locations page site filter and every site picker. Creating,
/// editing, deleting and re-permissioning sites is administrator-only (Administration ▸ Sites).
/// </summary>
public sealed class SitesController(SiteService sites, RequestSiteAccess siteAccess) : ApiControllerBase
{
    /// <summary>Sites visible to the current user (read or write), default first.</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<SiteDto>> Get()
    {
        var access = siteAccess.Current;
        var counts = sites.LocationCounts();
        return sites.GetAll()
            .Where(s => access.CanRead(s.Id))
            .Select(s => ToDto(s, access.LevelFor(s.Id), counts.GetValueOrDefault(s.Id)))
            .ToList();
    }

    [HttpPost]
    [ApiAuth(RequireAdmin = true)]
    public ActionResult<SiteDto> Create([FromBody] SaveSiteRequest req)
    {
        try
        {
            var site = sites.Create(req.Name, req.Description);
            return ToDto(site, SiteAccessLevel.Write, 0);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    [ApiAuth(RequireAdmin = true)]
    public ActionResult<SiteDto> Update(int id, [FromBody] SaveSiteRequest req)
    {
        try
        {
            var site = sites.Update(id, req.Name, req.Description);
            if (site is null) return NotFound(new { error = "Site not found." });
            return ToDto(site, SiteAccessLevel.Write, sites.LocationCounts().GetValueOrDefault(id));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>Delete a site. Its locations (and their cards) move to <paramref name="moveToSiteId"/>
    /// (default: the default site). The default site can't be deleted.</summary>
    [HttpDelete("{id:int}")]
    [ApiAuth(RequireAdmin = true)]
    public IActionResult Delete(int id, [FromQuery] int? moveToSiteId = null)
    {
        try
        {
            return sites.Delete(id, moveToSiteId) ? NoContent() : NotFound(new { error = "Site not found." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>The users and roles granted access to a site.</summary>
    [HttpGet("{id:int}/grants")]
    [ApiAuth(RequireAdmin = true)]
    public ActionResult<IReadOnlyList<SiteGrantDto>> Grants(int id)
    {
        if (sites.Get(id) is null) return NotFound(new { error = "Site not found." });
        return sites.GetGrants(id)
            .Select(g => new SiteGrantDto(g.PrincipalType.ToString(), g.PrincipalId, g.Level.ToString()))
            .ToList();
    }

    /// <summary>Replace a site's whole access list. Takes effect on each affected user's next request.</summary>
    [HttpPut("{id:int}/grants")]
    [ApiAuth(RequireAdmin = true)]
    public IActionResult SetGrants(int id, [FromBody] SetSiteGrantsRequest req)
    {
        var parsed = new List<(SitePrincipalType, int, SiteAccessLevel)>();
        foreach (var g in req.Grants)
        {
            if (!Enum.TryParse<SitePrincipalType>(g.PrincipalType, ignoreCase: true, out var type)
                || !Enum.TryParse<SiteAccessLevel>(g.Level, ignoreCase: true, out var level))
                return BadRequest(new { error = $"Invalid grant '{g.PrincipalType}:{g.PrincipalId}:{g.Level}'." });
            parsed.Add((type, g.PrincipalId, level));
        }

        try
        {
            sites.SetGrants(id, parsed);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Site not found." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    internal static SiteDto ToDto(Site s, SiteAccessLevel level, int locationCount) =>
        new(s.Id, s.Name, s.Description, s.IsDefault, s.SortOrder, level.ToString(), locationCount);
}
