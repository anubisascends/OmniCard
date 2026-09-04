using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Api;

/// <summary>Set browser: the list of a game's sets, and the per-set completion checklist.</summary>
public sealed class SetsController(
    IEnumerable<ICardGameService> gameServices,
    ISetChecklistService checklistService,
    ISetChecklistPdfExporter wantListPdf) : ApiControllerBase
{
    /// <summary>Printable want-list PDF (unowned cards) for a set.</summary>
    [HttpGet("{game}/{setCode}/wantlist.pdf")]
    public async Task<IActionResult> WantList(string game, string setCode)
    {
        if (!Enum.TryParse<CardGame>(game, ignoreCase: true, out var g))
            return NotFound();
        var checklist = await checklistService.BuildAsync(g, setCode);
        var report = checklistService.BuildWantListReport(checklist);
        var bytes = TempFile.Produce(".pdf", p => wantListPdf.Export(report, p));
        return File(bytes, "application/pdf", $"wantlist-{setCode}.pdf");
    }

    private ICardGameService? Game(string game) =>
        Enum.TryParse<CardGame>(game, ignoreCase: true, out var g)
            ? gameServices.FirstOrDefault(s => s.Game == g)
            : null;

    /// <summary>All sets available for a game, newest catalog order.</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<SetInfoDto>> Get([FromQuery] string game)
    {
        var svc = Game(game);
        if (svc is null) return NotFound(new { error = $"Unknown game '{game}'." });
        return svc.GetAvailableSets().Select(DtoMapping.ToDto).ToList();
    }

    /// <summary>The ownership checklist for one set (every printing + owned quantity + prices).</summary>
    [HttpGet("{game}/{setCode}")]
    public async Task<ActionResult<SetChecklistDto>> Checklist(string game, string setCode)
    {
        if (!Enum.TryParse<CardGame>(game, ignoreCase: true, out var g))
            return NotFound(new { error = $"Unknown game '{game}'." });

        var checklist = await checklistService.BuildAsync(g, setCode);
        return DtoMapping.ToDto(checklist);
    }
}
