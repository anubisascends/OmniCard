using Microsoft.AspNetCore.Mvc;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api;

/// <summary>
/// Read-only binder view for the SPA: the current two-page spread, its slots (with placed cards),
/// and the pagination strip. Reuses <see cref="BinderStateBuilder"/> — the exact spread/slot math
/// the desktop binder and the existing web editor use — so the layout is identical.
/// </summary>
public sealed class BinderController(BinderStateBuilder stateBuilder) : ApiControllerBase
{
    /// <summary>The binder state for <paramref name="id"/> at the given <paramref name="spread"/>
    /// (0-based; 0 shows page 1 alone on the right).</summary>
    [HttpGet("{id:int}")]
    public ActionResult<BinderStateDto> Get(int id, [FromQuery] int spread = 0)
    {
        try
        {
            return stateBuilder.BuildState(id, spread);
        }
        catch (InvalidOperationException)
        {
            // GetBinderLayout throws when the container isn't a binder / doesn't exist.
            return NotFound(new { error = $"No binder with id {id}." });
        }
    }
}
