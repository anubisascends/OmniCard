using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Security;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services.ScanValidation;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Scan validation on the Scan page: check the scan list against a search query (cards that
/// don't match are flagged) and the saved per-game validation rules. Checking is read-only, so anyone who
/// can scan may do it — including someone viewing another reviewer's batch; saving or deleting a shared
/// rule needs <see cref="Permissions.ScanCommit"/>.</summary>
[ApiController]
[ApiAuth]
[Route("api/scan")]
public sealed class ScanValidationController(ScanValidationService validation) : ControllerBase
{
    [HttpGet("validation-rules")]
    [RequirePermission(Permissions.ScanView)]
    public ActionResult<IReadOnlyList<ScanValidationRuleDto>> List([FromQuery] string game)
    {
        if (LocationsController.ParseGame(game) is not { } g)
            return BadRequest(new { error = $"Unknown game '{game}'" });
        return Ok(validation.List(g));
    }

    /// <summary>Save a rule; one with the same name for the game is replaced.</summary>
    [HttpPost("validation-rules")]
    [RequirePermission(Permissions.ScanCommit)]
    public ActionResult<ScanValidationRuleDto> Save([FromBody] ScanValidationRuleInput input)
    {
        try
        {
            return Ok(validation.Save(input));
        }
        catch (ScanValidationRuleException ex)
        {
            return BadRequest(new { error = ex.Message, errors = ex.Errors });
        }
    }

    [HttpDelete("validation-rules/{id:int}")]
    [RequirePermission(Permissions.ScanCommit)]
    public IActionResult Delete(int id) => validation.Delete(id) ? NoContent() : NotFound();

    /// <summary>The scanned cards that don't match the query (an invalid query returns its errors instead).</summary>
    [HttpPost("validate")]
    [RequirePermission(Permissions.ScanView)]
    public ActionResult<ScanValidationResultDto> Validate([FromBody] ScanValidateRequest request)
    {
        if (LocationsController.ParseGame(request.Game) is not { } game)
            return BadRequest(new { error = $"Unknown game '{request.Game}'" });
        return Ok(validation.Check(game, request.Query, request.Items));
    }
}
