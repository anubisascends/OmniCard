using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using OmniCard.Shared.Sites;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api.Infrastructure;

/// <summary>
/// Gates an action on the signed-in user's access to the <see cref="Site"/>(s) the request touches.
/// Runs after model binding, so it reads ids straight from the action's arguments:
/// <list type="bullet">
/// <item><see cref="Location"/> — name(s) of a location/container id (an action parameter, or a
/// property on a bound body record), comma-separated for several.</item>
/// <item><see cref="Lots"/> — name(s) of a card/lot id or id list, same lookup.</item>
/// </list>
/// A site the user can't read answers <c>404</c> (its contents stay invisible); a readable site that
/// needs <see cref="SiteAccessLevel.Write"/> but is only readable answers <c>403</c>. Ids that don't
/// resolve are skipped so the action's own not-found handling still applies. Admins always pass.
///
/// <para>Layered on top of <see cref="RequirePermissionAttribute"/>: the permission says what kind of
/// action the user may take, the site access says where.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireSiteAccessAttribute(SiteAccessLevel level) : Attribute, IActionFilter
{
    public SiteAccessLevel Level { get; } = level;

    /// <summary>Argument/property name(s) holding a location (container) id.</summary>
    public string? Location { get; init; }

    /// <summary>Argument/property name(s) holding a lot (card) id or a list of them.</summary>
    public string? Lots { get; init; }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var access = context.HttpContext.RequestServices.GetRequiredService<RequestSiteAccess>();
        if (access.Current.IsUnrestricted)
            return;

        var sites = new List<int>();
        foreach (var id in ResolveIds(context.ActionArguments, Location))
            if (access.SiteOfLocation(id) is int site)
                sites.Add(site);
        var lotIds = ResolveIds(context.ActionArguments, Lots).ToList();
        if (lotIds.Count > 0)
            sites.AddRange(access.SitesOfLots(lotIds));

        context.Result = Check(access.Current, sites, Level);
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    /// <summary>The result to short-circuit with, or null when every site passes.</summary>
    internal static IActionResult? Check(SiteAccess access, IEnumerable<int> sites, SiteAccessLevel level)
    {
        foreach (var site in sites.Distinct())
        {
            if (!access.CanRead(site))
                return new NotFoundObjectResult(new { error = "Not found." });
            if (level == SiteAccessLevel.Write && !access.CanWrite(site))
                return Forbidden();
        }
        return null;
    }

    /// <summary>The standard 403 for a site the user can see but not change.</summary>
    public static ObjectResult Forbidden() =>
        new(new { error = "You only have read access to that site." }) { StatusCode = 403 };

    /// <summary>Collects int ids named by <paramref name="names"/> from the bound arguments: a matching
    /// argument by name, else a matching public property on any complex argument (e.g. a request record).</summary>
    internal static IEnumerable<int> ResolveIds(IDictionary<string, object?> args, string? names)
    {
        if (string.IsNullOrWhiteSpace(names))
            yield break;

        foreach (var raw in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var direct = args.FirstOrDefault(a => string.Equals(a.Key, raw, StringComparison.OrdinalIgnoreCase));
            if (direct.Key is not null)
            {
                foreach (var id in Extract(direct.Value)) yield return id;
                continue;
            }

            foreach (var arg in args.Values)
            {
                if (arg is null || arg is string || arg.GetType().IsPrimitive) continue;
                var prop = arg.GetType().GetProperty(raw,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop is null) continue;
                foreach (var id in Extract(prop.GetValue(arg))) yield return id;
            }
        }
    }

    private static IEnumerable<int> Extract(object? value)
    {
        switch (value)
        {
            case null:
                yield break;
            case int i:
                yield return i;
                yield break;
            case IEnumerable e and not string:
                foreach (var item in e)
                    if (item is int n) yield return n;
                yield break;
        }
    }
}
