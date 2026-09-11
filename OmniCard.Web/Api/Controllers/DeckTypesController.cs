using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Storage;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Api.Mapping;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Per-game deck formats (Commander, Standard, …): the seeded built-ins plus user-created
/// custom types. Backs the deck-box game/type pickers and the Settings deck-types editor.</summary>
[Route("api/deck-types")]
public sealed class DeckTypesController(IDeckTypeService deckTypes) : ApiControllerBase
{
    /// <summary>Deck types for a game (built-in + custom). Empty when the game is missing/invalid.</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<DeckTypeDto>> Get([FromQuery] string game)
    {
        if (!Enum.TryParse<CardGame>(game, ignoreCase: true, out var g))
            return BadRequest(new { error = $"Invalid game '{game}'." });
        return deckTypes.GetForGame(g).Select(DtoMapping.ToDto).ToList();
    }

    /// <summary>Create a custom deck type. 400 on invalid game/blank name, 409 on a duplicate name.</summary>
    [HttpPost]
    public ActionResult<DeckTypeDto> Create([FromBody] DeckTypeUpsertRequest req)
    {
        if (!Enum.TryParse<CardGame>(req.Game, ignoreCase: true, out var g))
            return BadRequest(new { error = $"Invalid game '{req.Game}'." });
        try
        {
            var created = deckTypes.Create(FromRequest(g, req));
            return DtoMapping.ToDto(created);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>Update a deck type's name/rules (built-ins may be edited too). 409 on a duplicate name.</summary>
    [HttpPut("{id:int}")]
    public IActionResult Update(int id, [FromBody] DeckTypeUpsertRequest req)
    {
        try
        {
            // Game is fixed for an existing row; the service ignores it, so any value is fine here.
            deckTypes.Update(id, FromRequest(CardGame.Mtg, req));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        return NoContent();
    }

    /// <summary>Delete a deck type. Deck boxes referencing it have their reference cleared.</summary>
    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        try
        {
            deckTypes.Delete(id);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        return NoContent();
    }

    private static DeckType FromRequest(CardGame game, DeckTypeUpsertRequest req) => new()
    {
        Game = game,
        Name = req.Name,
        DeckSizeMin = req.DeckSizeMin,
        DeckSizeMax = req.DeckSizeMax,
        MaxCopiesPerCard = req.MaxCopiesPerCard,
        Singleton = req.Singleton,
        BasicLandsExempt = req.BasicLandsExempt,
        CommanderSlots = req.CommanderSlots,
    };
}
