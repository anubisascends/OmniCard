using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services.TagRules;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Auto-tagging rules (Settings ▸ Tag rules). Admin only: rules tag every user's scans and
/// imports, and "Run now" writes tags across the whole collection (every site). Applying rules to new
/// cards happens in the scan/import endpoints, open to anyone allowed to scan or import.</summary>
[ApiController]
[ApiAuth(RequireAdmin = true)]
[Route("api/tag-rules")]
public sealed class TagRulesController(TagRuleService rules) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<TagRuleDto>> List([FromQuery] string? game)
    {
        if (game is not null && LocationsController.ParseGame(game) is null)
            return BadRequest(new { error = $"Unknown game '{game}'" });
        return Ok(rules.List(LocationsController.ParseGame(game)));
    }

    [HttpPost]
    public ActionResult<TagRuleDto> Create([FromBody] TagRuleInput input)
    {
        try
        {
            return Ok(rules.Create(input));
        }
        catch (TagRuleValidationException ex)
        {
            return BadRequest(new { error = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPut("{id:int}")]
    public ActionResult<TagRuleDto> Update(int id, [FromBody] TagRuleInput input)
    {
        try
        {
            return rules.Update(id, input) is { } dto ? Ok(dto) : NotFound();
        }
        catch (TagRuleValidationException ex)
        {
            return BadRequest(new { error = ex.Message, errors = ex.Errors });
        }
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => rules.Delete(id) ? NoContent() : NotFound();

    /// <summary>Check a draft query as the admin types it.</summary>
    [HttpPost("validate")]
    public ActionResult<TagRuleValidationDto> Validate([FromBody] TagRuleValidateRequest request)
    {
        if (LocationsController.ParseGame(request.Game) is not { } game)
            return BadRequest(new { error = $"Unknown game '{request.Game}'" });
        return Ok(new TagRuleValidationDto(rules.Validate(game, request.Query)));
    }

    /// <summary>How many owned cards a (possibly unsaved) rule matches and would change, with a sample.</summary>
    [HttpPost("preview")]
    public ActionResult<TagRulePreviewDto> Preview([FromBody] TagRulePreviewRequest request)
    {
        if (LocationsController.ParseGame(request.Game) is not { } game)
            return BadRequest(new { error = $"Unknown game '{request.Game}'" });
        return Ok(rules.Preview(game, request.Query, request.Tags));
    }

    /// <summary>Apply a saved rule to every owned card it matches (adds tags only).</summary>
    [HttpPost("{id:int}/run")]
    public ActionResult<TagRuleRunResultDto> Run(int id)
    {
        try
        {
            return rules.Run(id) is { } result ? Ok(result) : NotFound();
        }
        catch (TagRuleValidationException ex)
        {
            return BadRequest(new { error = ex.Message, errors = ex.Errors });
        }
    }
}
