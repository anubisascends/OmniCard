using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Interfaces;

namespace OmniCard.Web.Api;

/// <summary>App metadata for the SPA shell: the list of supported games (for the game selector).</summary>
public sealed class MetaController(IEnumerable<ICardGameService> gameServices) : ApiControllerBase
{
    [HttpGet("games")]
    public ActionResult<IReadOnlyList<GameDto>> Games() =>
        gameServices.Select(g => DtoMapping.ToDto(g.Game)).ToList();
}
