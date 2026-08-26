using Microsoft.AspNetCore.Mvc;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api;

/// <summary>
/// Write API for the web binder editor — the JSON backend the <c>/binder/{id}/edit</c> page calls.
/// Every endpoint maps to a command on the desktop <c>BinderViewModel</c>. Gated by the passphrase
/// (<see cref="BinderEditAuthAttribute"/>); all writes go through the writable inventory.db services
/// registered in <c>Program.cs</c>. Live eBay actions are intentionally out of scope here.
/// </summary>
[ApiController]
[Route("api/binder")]
[BinderEditAuth]
public sealed class BinderEditController : ControllerBase
{
    private readonly IStorageContainerService _containers;
    private readonly WebBinderCardService _binderCards;
    private readonly ITagService _tags;
    private readonly IListingService _listings;
    private readonly ICardService _cardService;
    private readonly BinderStateBuilder _state;

    public BinderEditController(
        IStorageContainerService containers,
        WebBinderCardService binderCards,
        ITagService tags,
        IListingService listings,
        ICardService cardService,
        BinderStateBuilder state)
    {
        _containers = containers;
        _binderCards = binderCards;
        _tags = tags;
        _listings = listings;
        _cardService = cardService;
        _state = state;
    }

    // ---------------------------------------------------------------- Read: spread + unplaced pool

    [HttpGet("state")]
    public IActionResult State(int containerId, int spreadIndex = 0)
        => Ok(_state.BuildState(containerId, spreadIndex));

    [HttpGet("unplaced")]
    public IActionResult Unplaced(int containerId, string? filter = null)
        => Ok(new { cards = _state.BuildUnplaced(containerId, filter) });

    [HttpGet("sheets")]
    public IActionResult Sheets(int containerId)
        => Ok(new { sheets = _containers.GetSheets(containerId) });

    // ---------------------------------------------------------------- Arrangement

    public sealed record AssignRequest(int LotId, int ContainerId, int Page, int Slot);

    [HttpPost("assign")]
    public IActionResult Assign([FromBody] AssignRequest r)
    {
        _containers.AssignCardToSlot(r.LotId, r.ContainerId, r.Page, r.Slot);
        return Ok(new { status = "ok" });
    }

    public sealed record UnassignRequest(int LotId);

    [HttpPost("unassign")]
    public IActionResult Unassign([FromBody] UnassignRequest r)
    {
        _containers.UnassignFromPage(r.LotId);
        return Ok(new { status = "ok" });
    }

    public sealed record LayoutRequest(int ContainerId, int SlotsPerPage, int Columns);

    [HttpPost("layout")]
    public IActionResult Layout([FromBody] LayoutRequest r)
    {
        if (r.SlotsPerPage <= 0 || r.Columns <= 0)
            return BadRequest(new { error = "Slots per page and columns must be positive." });
        _containers.SetSlotsPerPage(r.ContainerId, r.SlotsPerPage);
        _containers.SetColumns(r.ContainerId, r.Columns);
        return Ok(new { status = "ok" });
    }

    // ---------------------------------------------------------------- Pages

    public sealed record AddPageRequest(int ContainerId, string? Mode);

    [HttpPost("page/add")]
    public IActionResult AddPage([FromBody] AddPageRequest r)
    {
        var doubleSided = !string.Equals(r.Mode, "single", StringComparison.OrdinalIgnoreCase);
        _containers.AddBinderSheet(r.ContainerId, doubleSided);
        var totalPages = _containers.GetBinderLayout(r.ContainerId).TotalPages;
        // Jump to the spread containing the new last page (mirrors BinderViewModel.AddPage).
        return Ok(new { status = "ok", spreadIndex = totalPages / 2 });
    }

    public sealed record InsertPageRequest(int ContainerId, int InsertIndex, bool DoubleSided);

    [HttpPost("page/insert")]
    public IActionResult InsertPage([FromBody] InsertPageRequest r)
    {
        _containers.InsertBinderSheet(r.ContainerId, r.InsertIndex, r.DoubleSided);
        var sheets = _containers.GetSheets(r.ContainerId);
        var totalPages = _containers.GetBinderLayout(r.ContainerId).TotalPages;
        var insertedFirstPage = r.InsertIndex < sheets.Count ? sheets[r.InsertIndex].FirstPage : totalPages;
        var spreadIndex = insertedFirstPage <= 1 ? 0 : insertedFirstPage / 2;
        return Ok(new { status = "ok", spreadIndex });
    }

    public sealed record MovePageRequest(int ContainerId, int FromPage, int ToIndex);

    [HttpPost("page/move")]
    public IActionResult MovePage([FromBody] MovePageRequest r)
    {
        _containers.MoveBinderSheet(r.ContainerId, r.FromPage, r.ToIndex);
        var sheets = _containers.GetSheets(r.ContainerId);
        var landedFirstPage = r.ToIndex < sheets.Count ? sheets[r.ToIndex].FirstPage : sheets[^1].FirstPage;
        var spreadIndex = landedFirstPage <= 1 ? 0 : landedFirstPage / 2;
        return Ok(new { status = "ok", spreadIndex });
    }

    public sealed record RemovePageRequest(int ContainerId, int Page);

    [HttpPost("page/remove")]
    public IActionResult RemovePage([FromBody] RemovePageRequest r)
    {
        try
        {
            _containers.RemoveBinderSheet(r.ContainerId, r.Page);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        return Ok(new { status = "ok" });
    }

    public sealed record ShiftPageRequest(int ContainerId, int Page, int DeltaPages, string Scope);

    [HttpPost("page/shift")]
    public IActionResult ShiftPage([FromBody] ShiftPageRequest r)
    {
        if (!Enum.TryParse<BinderShiftScope>(r.Scope, ignoreCase: true, out var scope))
            return BadRequest(new { error = $"Unknown shift scope '{r.Scope}'." });
        try
        {
            _containers.ShiftPage(r.ContainerId, r.Page, r.DeltaPages, scope);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        return Ok(new { status = "ok" });
    }

    // ---------------------------------------------------------------- Card actions

    public sealed record IdsRequest(List<int> Ids);

    public sealed record MoveLocationRequest(List<int> Ids, int ContainerId, string? Section);

    [HttpPost("card/move-location")]
    public IActionResult MoveLocation([FromBody] MoveLocationRequest r)
    {
        _binderCards.MoveCardsToContainer(r.Ids, r.ContainerId, r.Section);
        return Ok(new { status = "ok" });
    }

    public sealed record ListRequest(List<int> Ids, string Channel, decimal Price, int Quantity);

    [HttpPost("card/list")]
    public IActionResult ListForSale([FromBody] ListRequest r)
    {
        if (!Enum.TryParse<SalesChannel>(r.Channel, ignoreCase: true, out var channel))
            return BadRequest(new { error = $"Unknown sales channel '{r.Channel}'." });
        if (r.Quantity <= 0 || r.Price < 0)
            return BadRequest(new { error = "Quantity must be positive and price non-negative." });
        _listings.ListForSale(r.Ids, channel, r.Price, r.Quantity);
        return Ok(new { status = "ok" });
    }

    [HttpPost("card/unlist")]
    public IActionResult Unlist([FromBody] IdsRequest r)
    {
        _listings.Unlist(r.Ids);
        return Ok(new { status = "ok" });
    }

    [HttpPost("card/mark-picked")]
    public IActionResult MarkPicked([FromBody] IdsRequest r)
    {
        try { _listings.MarkPicked(r.Ids); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        return Ok(new { status = "ok" });
    }

    public sealed record ConditionRequest(List<int> Ids, string Value);

    [HttpPost("card/condition")]
    public IActionResult SetCondition([FromBody] ConditionRequest r)
    {
        _binderCards.SetCondition(r.Ids, r.Value);
        return Ok(new { status = "ok" });
    }

    public sealed record FoilRequest(List<int> Ids, bool IsFoil);

    [HttpPost("card/foil")]
    public IActionResult SetFoil([FromBody] FoilRequest r)
    {
        _binderCards.SetFoil(r.Ids, r.IsFoil);
        return Ok(new { status = "ok" });
    }

    [HttpPost("card/delete")]
    public IActionResult Delete([FromBody] IdsRequest r)
    {
        foreach (var id in r.Ids)
            _binderCards.DeleteCollectionCard(id);
        return Ok(new { status = "ok" });
    }

    public sealed record TagRequest(List<int> Ids, string Tag, bool Apply);

    [HttpPost("card/tags")]
    public IActionResult Tags([FromBody] TagRequest r)
    {
        var name = r.Tag.Trim();
        if (name.Length == 0)
            return BadRequest(new { error = "Tag name is required." });
        if (r.Apply) _tags.AddTagToLots(r.Ids, name);
        else _tags.RemoveTagFromLots(r.Ids, name);
        return Ok(new { status = "ok" });
    }

    public sealed record UpdateCardRequest(int Id, string Condition, bool IsFoil, string? FoilType, decimal? PurchasePrice);

    [HttpPost("card/update")]
    public IActionResult UpdateCard([FromBody] UpdateCardRequest r)
    {
        // Load the current DTO (identity + placement) and apply only the editor-editable fields, so
        // the card keeps its page/slot/location and catalog identity.
        var card = _binderCards.GetCollectionCards([r.Id]).FirstOrDefault();
        if (card is null)
            return NotFound(new { error = "Card not found." });

        card.Condition = string.IsNullOrWhiteSpace(r.Condition) ? "NM" : r.Condition;
        card.IsFoil = r.IsFoil;
        card.FoilType = r.IsFoil ? r.FoilType : null;
        card.PurchasePrice = r.PurchasePrice;
        _binderCards.UpdateCollectionCard(card);
        return Ok(new { status = "ok" });
    }

    public sealed record AddMissingRequest(
        int ContainerId, int Page, int Slot,
        int Game, string GameSpecificId, string Name, string SetCode, string SetName,
        string CollectorNumber, string Rarity, string? ImageUri,
        string Condition, bool IsFoil, string? FoilType, decimal? PurchasePrice);

    [HttpPost("card/add-missing")]
    public IActionResult AddMissing([FromBody] AddMissingRequest r)
    {
        var game = (CardGame)r.Game;
        var match = new CardMatch
        {
            GameSpecificId = r.GameSpecificId,
            Name = r.Name,
            SetCode = r.SetCode,
            SetName = r.SetName,
            CollectorNumber = r.CollectorNumber,
            Rarity = r.Rarity,
            ImageUri = r.ImageUri,
        };
        var foilType = r.IsFoil ? (r.FoilType ?? FoilTypes.BasicFoilType(game)) : null;
        _binderCards.AddMissingCardToSlot(match, game,
            string.IsNullOrWhiteSpace(r.Condition) ? "NM" : r.Condition,
            r.IsFoil, foilType, r.PurchasePrice, r.ContainerId, r.Page, r.Slot);
        return Ok(new { status = "ok" });
    }

    // ---------------------------------------------------------------- Pickers / lookups

    [HttpGet("locations")]
    public IActionResult Locations()
    {
        var containers = _containers.GetAll()
            .Select(c => new
            {
                c.Id,
                c.Name,
                Type = c.ContainerType.ToString(),
                NeedsSection = c.ContainerType == ContainerType.Box,
            })
            .ToList();
        return Ok(new { locations = containers });
    }

    [HttpGet("catalog/search")]
    public IActionResult CatalogSearch(int game, string? query, string? set, string? cn, int max = 20)
    {
        CardGame g = (CardGame)game;
        ICardGameService svc;
        try { svc = _cardService.GetGameService(g); }
        catch { return BadRequest(new { error = $"Game {game} is not available." }); }

        // Compose the same set:/cn: tokens the desktop ManualAdd dialog uses.
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query)) parts.Add(query.Trim());
        if (!string.IsNullOrWhiteSpace(set)) parts.Add($"set:{set.Trim()}");
        if (!string.IsNullOrWhiteSpace(cn)) parts.Add($"cn:{cn.Trim()}");
        var composed = string.Join(' ', parts);
        if (composed.Length == 0)
            return Ok(new { results = Array.Empty<object>() });

        var results = svc.SearchCards(composed, max).Select(m => new
        {
            m.GameSpecificId,
            m.Name,
            m.SetCode,
            m.SetName,
            m.CollectorNumber,
            m.Rarity,
            m.ImageUri,
        });
        return Ok(new { results });
    }

    [HttpGet("foil-types")]
    public IActionResult FoilTypesFor(int game)
        => Ok(new { foilTypes = FoilTypes.ForGame((CardGame)game) });

    [HttpGet("games")]
    public IActionResult Games()
        => Ok(new { games = _cardService.AvailableGames.Select(g => new { Id = (int)g, Name = g.ToString() }) });
}
