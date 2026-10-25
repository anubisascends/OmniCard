using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Security;
using OmniCard.Shared.Sites;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services;
using OmniCard.Web.Services.Labels;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Card labels: the shared label templates (designed in Settings ▸ Labels) and the card data a
/// print run fills them with. The PDF itself is rendered in the browser from the template. Anyone who may
/// print can read the templates; changing them needs <see cref="Permissions.LabelsEdit"/>.</summary>
[ApiController]
[ApiAuth]
[Route("api/labels")]
public sealed class LabelsController(
    LabelTemplateService templates,
    LabelDataService labelData,
    RequestSiteAccess siteAccess) : ControllerBase
{
    [HttpGet("templates")]
    [RequirePermission(Permissions.LabelsPrint, Permissions.LabelsEdit)]
    public ActionResult<IReadOnlyList<LabelTemplateDto>> List() => Ok(templates.List());

    [HttpGet("templates/{id:int}")]
    [RequirePermission(Permissions.LabelsPrint, Permissions.LabelsEdit)]
    public ActionResult<LabelTemplateDto> Get(int id) => templates.Get(id) is { } t ? Ok(t) : NotFound();

    [HttpPost("templates")]
    [RequirePermission(Permissions.LabelsEdit)]
    public ActionResult<LabelTemplateDto> Create([FromBody] LabelTemplateInput input)
    {
        try
        {
            return Ok(templates.Create(input));
        }
        catch (LabelTemplateException ex)
        {
            return BadRequest(new { error = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPut("templates/{id:int}")]
    [RequirePermission(Permissions.LabelsEdit)]
    public ActionResult<LabelTemplateDto> Update(int id, [FromBody] LabelTemplateInput input)
    {
        try
        {
            return templates.Update(id, input) is { } t ? Ok(t) : NotFound();
        }
        catch (LabelTemplateException ex)
        {
            return BadRequest(new { error = ex.Message, errors = ex.Errors });
        }
    }

    [HttpDelete("templates/{id:int}")]
    [RequirePermission(Permissions.LabelsEdit)]
    public IActionResult Delete(int id) => templates.Delete(id) ? NoContent() : NotFound();

    /// <summary>The label values for the selected cards, or for every card in a location.</summary>
    [HttpPost("data")]
    [RequirePermission(Permissions.LabelsPrint)]
    [RequireSiteAccess(SiteAccessLevel.Read, Lots = "LotIds", Location = "ContainerId")]
    public ActionResult<LabelDataDto> Data([FromBody] LabelDataRequest request) =>
        Ok(labelData.Build(request, siteAccess.Current.ReadableSiteIds));
}
