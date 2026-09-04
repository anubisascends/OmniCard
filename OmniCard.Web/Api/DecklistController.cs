using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Api;

/// <summary>Check a decklist (pasted text or a Moxfield/Archidekt URL) against the collection —
/// owned vs. missing, with an estimated cost to complete. Read-only.</summary>
public sealed class DecklistController(IDecklistService decklists) : ApiControllerBase
{
    [HttpPost("check")]
    public async Task<ActionResult<DecklistCheckDto>> Check([FromBody] DecklistCheckRequest req)
    {
        if (!Enum.TryParse<CardGame>(req.Game, ignoreCase: true, out var game))
            return BadRequest(new { error = $"Unknown game '{req.Game}'." });

        string deckName;
        string source;
        List<DecklistEntry> entries;

        if (!string.IsNullOrWhiteSpace(req.Url))
        {
            var fetched = await decklists.FetchDecklistAsync(req.Url);
            if (fetched is null)
                return BadRequest(new { error = "Couldn't fetch that decklist URL." });
            (deckName, entries) = fetched.Value;
            source = req.Url;
        }
        else if (!string.IsNullOrWhiteSpace(req.Text))
        {
            (deckName, entries) = decklists.ParseDecklistText(req.Text);
            source = "pasted";
        }
        else
        {
            return BadRequest(new { error = "Provide a decklist URL or pasted text." });
        }

        var result = decklists.CheckAgainstCollection(deckName, source, entries, game);
        return new DecklistCheckDto
        {
            DeckName = result.DeckName,
            TotalOwned = result.TotalOwned,
            TotalMissing = result.TotalMissing,
            TotalCards = result.TotalCards,
            EstimatedCost = result.EstimatedCost,
            Owned = result.OwnedEntries
                .Select(e => new DecklistEntryDto(e.CardName, e.QuantityNeeded, e.SetCode, null, e.ImageUri))
                .ToList(),
            Missing = result.MissingEntries
                .Select(e => new DecklistEntryDto(e.CardName, e.QuantityNeeded, e.SetCode, e.MarketPrice, e.ImageUri))
                .ToList(),
        };
    }
}
