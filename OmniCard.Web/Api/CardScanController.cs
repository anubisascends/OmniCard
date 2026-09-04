using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api;

/// <summary>
/// Server-side card scanning for the SPA: upload an image, match it against a game's catalog
/// (<c>WebScanMatchingService</c>), search the catalog to correct a bad match, and commit confirmed
/// cards into a storage location. This replaces the desktop the migration is retiring — matching runs
/// here, not relayed to a WPF app. Routed under <c>api/scan/*</c> and passphrase-gated
/// (<see cref="ApiAuth"/>); the legacy phone→desktop <c>ScanController</c> is left untouched.
/// </summary>
[ApiController]
[ApiAuth]
[Route("api/scan")]
public sealed class CardScanController(
    WebScanMatchingService matcher,
    ICardService cardService,
    WebBinderCardService binderCards,
    ILogger<CardScanController> logger) : ControllerBase
{
    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png" };
    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    /// <summary>Match one uploaded card image against <paramref name="game"/>'s catalog.</summary>
    [HttpPost("match")]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<ActionResult<ScanMatchDto>> Match(
        IFormFile image, [FromForm] string game, [FromForm] bool isFoil, CancellationToken ct)
    {
        if (image is null || image.Length == 0)
            return BadRequest(new { error = "No image provided" });
        if (!AllowedContentTypes.Contains(image.ContentType))
            return BadRequest(new { error = "Only JPEG and PNG images are accepted" });
        if (image.Length > MaxFileSize)
            return BadRequest(new { error = "Image exceeds 10 MB limit" });
        if (LocationsController.ParseGame(game) is not { } parsedGame)
            return BadRequest(new { error = $"Unknown game '{game}'" });

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms, ct);

        var result = await matcher.MatchAsync(ms.ToArray(), parsedGame, isFoil, ct);
        return Ok(result);
    }

    /// <summary>Catalog search for the correction screen (read-only, scoped to one game).</summary>
    [HttpGet("search")]
    public ActionResult<IReadOnlyList<ScanSearchResultDto>> Search([FromQuery] string game, [FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(Array.Empty<ScanSearchResultDto>());
        if (LocationsController.ParseGame(game) is not { } parsedGame)
            return BadRequest(new { error = $"Unknown game '{game}'" });

        var matches = cardService.GetGameService(parsedGame).SearchCards(q, 20);
        var results = matches.Select(m => new ScanSearchResultDto(
            m.GameSpecificId, m.Name, m.SetCode, m.SetName, m.CollectorNumber, m.Rarity, m.ImageUri)).ToList();
        return Ok(results);
    }

    /// <summary>Write a batch of confirmed scans into a storage location as owned lots.</summary>
    [HttpPost("commit")]
    public ActionResult<ScanCommitResultDto> Commit([FromBody] ScanCommitRequest request)
    {
        if (request.ContainerId <= 0)
            return BadRequest(new { error = "A target location is required" });
        if (request.Items.Count == 0)
            return BadRequest(new { error = "No cards to commit" });

        var cards = new List<CollectionCard>(request.Items.Count);
        foreach (var item in request.Items)
        {
            if (LocationsController.ParseGame(item.Game) is not { } game)
                return BadRequest(new { error = $"Unknown game '{item.Game}'" });

            cards.Add(new CollectionCard
            {
                Game = game,
                GameCardId = item.GameCardId,
                Name = item.Name,
                SetCode = item.SetCode,
                SetName = item.SetName,
                Number = item.CollectorNumber,
                Rarity = item.Rarity,
                ImageUri = item.ImageUri,
                Condition = string.IsNullOrWhiteSpace(item.Condition) ? "NM" : item.Condition,
                IsFoil = item.IsFoil,
                FoilType = item.IsFoil ? FoilTypes.BasicFoilType(game) : null,
                Quantity = Math.Max(1, item.Quantity),
                PurchasePrice = item.PurchasePrice,
                DateAdded = DateTime.UtcNow,
                ContainerId = request.ContainerId,
            });
        }

        // skipDuplicates:false — a scanned card is a real physical copy; adding a second copy of a card
        // already owned must create/extend a lot, not be silently dropped as a "duplicate".
        var imported = binderCards.ImportCollectionCards(cards, skipDuplicates: false);
        logger.LogInformation("Committed {Count} scanned card(s) to location {LocationId}", imported, request.ContainerId);
        return Ok(new ScanCommitResultDto(imported));
    }
}
