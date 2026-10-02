using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Audit;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Security;
using OmniCard.Shared.Sites;
using OmniCard.Shared.Storage;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Check a decklist (pasted text or a Moxfield/Archidekt URL) against the collection —
/// owned vs. missing, with an estimated cost to complete and the exact copies to pull. Also prints the
/// pull / missing checklists and moves a fully-owned deck's picks into a deck box.</summary>
public sealed class DecklistController(
    IDecklistService decklists,
    IDecklistPrintExporter printExporter,
    WebBinderCardService binderCards,
    IDbContextFactory<OmniCardDbContext> dbFactory,
    RequestSiteAccess siteAccess) : ApiControllerBase
{
    [HttpPost("check")]
    [RequirePermission(Permissions.CollectionView)]
    public async Task<ActionResult<DecklistCheckDto>> Check([FromBody] DecklistCheckRequest req)
    {
        var (result, game, error) = await RunCheckAsync(req);
        if (error is not null) return error;
        return ToDto(result!, game);
    }

    /// <summary>Printable pull list: each owned copy to pull, grouped by location, with a tick-box.
    /// Takes the same request as <see cref="Check"/> (the check is re-run, so the print always reflects
    /// the collection as it is now).</summary>
    [HttpPost("pull-list.pdf")]
    [RequirePermission(Permissions.CollectionView)]
    public async Task<IActionResult> PullListPdf([FromBody] DecklistCheckRequest req)
    {
        var (result, _, error) = await RunCheckAsync(req);
        if (error is not null) return error;
        var bytes = TempFile.Produce(".pdf", p => printExporter.ExportPullList(result!, p));
        return File(bytes, "application/pdf", $"pull-list-{SafeFileName(result!.DeckName)}.pdf");
    }

    /// <summary>Printable missing list: cards not in the collection, with prices and a tick-box.</summary>
    [HttpPost("missing-list.pdf")]
    [RequirePermission(Permissions.CollectionView)]
    public async Task<IActionResult> MissingListPdf([FromBody] DecklistCheckRequest req)
    {
        var (result, _, error) = await RunCheckAsync(req);
        if (error is not null) return error;
        var bytes = TempFile.Produce(".pdf", p => printExporter.ExportMissingList(result!, p));
        return File(bytes, "application/pdf", $"missing-{SafeFileName(result!.DeckName)}.pdf");
    }

    /// <summary>Moves a decklist check's picks into a deck box (stacked lots are split so only the
    /// needed copies move). Rejects non-deck-box targets and deck boxes locked to another game.</summary>
    [HttpPost("move-to-deck-box")]
    [RequirePermission(Permissions.CollectionEdit)]
    [RequireSiteAccess(SiteAccessLevel.Write, Location = "ContainerId")]
    public ActionResult<DecklistMoveResultDto> MoveToDeckBox([FromBody] DecklistMoveRequest req)
    {
        if (req.Picks.Count == 0)
            return BadRequest(new { error = "No cards to move." });
        // Pulling a pick out of its current location is a write to that location's site.
        if (RequireSiteAccessAttribute.Check(siteAccess.Current,
                siteAccess.SitesOfLots(req.Picks.Select(p => p.LotId)), SiteAccessLevel.Write) is { } denied)
            return (ActionResult)denied;

        using (var ctx = dbFactory.CreateDbContext())
        {
            var target = ctx.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == req.ContainerId);
            if (target is null)
                return NotFound(new { error = "That deck box no longer exists." });
            if (target.ContainerType != ContainerType.DeckBox)
                return BadRequest(new { error = $"\"{target.Name}\" isn't a deck box." });
        }

        try
        {
            var moved = binderCards.MoveQuantitiesToContainer(
                req.Picks.Select(p => (p.LotId, p.Quantity)).ToList(), req.ContainerId);
            return new DecklistMoveResultDto(moved);
        }
        catch (DeckBoxGameMismatchException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    private async Task<(DecklistCheckResult? Result, CardGame Game, ActionResult? Error)> RunCheckAsync(DecklistCheckRequest req)
    {
        if (!Enum.TryParse<CardGame>(req.Game, ignoreCase: true, out var game))
            return (null, default, BadRequest(new { error = $"Unknown game '{req.Game}'." }));

        string deckName;
        string source;
        List<DecklistEntry> entries;

        if (!string.IsNullOrWhiteSpace(req.Url))
        {
            var fetched = await decklists.FetchDecklistAsync(req.Url);
            if (fetched is null)
                return (null, game, BadRequest(new { error = "Couldn't fetch that decklist URL." }));
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
            return (null, game, BadRequest(new { error = "Provide a decklist URL or pasted text." }));
        }

        return (decklists.CheckAgainstCollection(deckName, source, entries, game, siteAccess.Current.ReadableSiteIds), game, null);
    }

    private static DecklistCheckDto ToDto(DecklistCheckResult result, CardGame game) => new()
    {
        DeckName = result.DeckName,
        Game = game.ToString(),
        TotalOwned = result.TotalOwned,
        TotalMissing = result.TotalMissing,
        TotalCards = result.TotalCards,
        EstimatedCost = result.EstimatedCost,
        Owned = result.OwnedEntries
            .Select(e => new DecklistEntryDto(e.CardName, e.QuantityNeeded, e.SetCode, null, e.ImageUri,
                e.CollectorNumber, (e.Picks ?? []).Select(ToDto).ToList()))
            .ToList(),
        Missing = result.MissingEntries
            .Select(e => new DecklistEntryDto(e.CardName, e.QuantityNeeded, e.SetCode, e.MarketPrice, e.ImageUri,
                e.CollectorNumber))
            .ToList(),
    };

    private static DecklistPickDto ToDto(DecklistPick p) => new(
        p.LotId, p.ContainerId, p.ContainerName, p.ContainerType?.ToString(), p.Page, p.Slot, p.Section,
        p.SetCode, p.CollectorNumber, p.IsFoil, p.Condition, p.Quantity, p.IsListed);

    internal static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "deck" : cleaned;
    }
}
