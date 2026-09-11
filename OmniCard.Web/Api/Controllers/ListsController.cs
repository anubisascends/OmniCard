using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Web.Services;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Lists;
using OmniCard.Web.Api.Infrastructure;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Saved card lists (want-lists, buy-lists, trade binders…). CRUD + item management via
/// <see cref="IListService"/>; committing a list into a location goes through
/// <see cref="WebBinderCardService"/> (the web-safe write path), since the desktop's
/// <c>CommitToLocation</c> relies on a WPF-only <c>ICardService</c> method.</summary>
public sealed class ListsController(
    IListService lists,
    IDecklistService decklists,
    WebBinderCardService binderCards) : ApiControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<CardListDto>> Get([FromQuery] string? game)
    {
        if (LocationsController.ParseGame(game) is not { } g)
            return BadRequest(new { error = "A game is required" });
        return lists.GetLists(g)
            .Select(l => new CardListDto(l.Id, l.Name, l.Game.ToString(), l.Notes, lists.GetItems(l.Id).Count))
            .ToList();
    }

    [HttpPost]
    public ActionResult<CardListDto> Create([FromBody] CreateListRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required" });
        if (LocationsController.ParseGame(request.Game) is not { } game)
            return BadRequest(new { error = $"Unknown game '{request.Game}'" });

        var created = lists.CreateList(request.Name.Trim(), game);
        return new CardListDto(created.Id, created.Name, created.Game.ToString(), created.Notes, 0);
    }

    [HttpPut("{id:int}")]
    public IActionResult Rename(int id, [FromBody] RenameRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required" });
        lists.RenameList(id, request.Name.Trim());
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        lists.DeleteList(id);
        return NoContent();
    }

    /// <summary>Fetch a Moxfield/Archidekt decklist by URL and add its cards to a list. Creates a new list
    /// (named after the deck) when <c>ListId</c> is null, otherwise appends to the existing list. Card names
    /// are resolved to printings via <see cref="IListService.AddCardsByName"/>; any that don't resolve come
    /// back in <c>UnresolvedNames</c>.</summary>
    [HttpPost("import-url")]
    public async Task<ActionResult<ImportListResultDto>> ImportUrl([FromBody] ImportListUrlRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(new { error = "A decklist URL is required" });

        var fetched = await decklists.FetchDecklistAsync(request.Url);
        if (fetched is null)
            return BadRequest(new { error = "Couldn't fetch that decklist URL. Supported: Moxfield, Archidekt." });

        var (deckName, entries) = fetched.Value;
        if (entries.Count == 0)
            return BadRequest(new { error = "That decklist has no cards." });

        CardList list;
        bool created;
        if (request.ListId is { } listId)
        {
            var existing = FindList(listId);
            if (existing is null)
                return NotFound();
            list = existing;
            created = false;
        }
        else
        {
            if (LocationsController.ParseGame(request.Game) is not { } game)
                return BadRequest(new { error = $"Unknown game '{request.Game}'" });
            var name = string.IsNullOrWhiteSpace(deckName) ? "Imported deck" : deckName;
            list = lists.CreateList(name, game);
            created = true;
        }

        var result = lists.AddCardsByName(list.Id, entries, ListItemSource.Url);
        return new ImportListResultDto(list.Id, list.Name, created, result.AddedCount, result.UnresolvedNames);
    }

    [HttpGet("{id:int}/items")]
    public ActionResult<IReadOnlyList<CardListItemDto>> Items(int id) =>
        lists.GetItems(id).Select(ToDto).ToList();

    [HttpDelete("items/{itemId:int}")]
    public IActionResult RemoveItem(int itemId)
    {
        lists.RemoveItem(itemId);
        return NoContent();
    }

    [HttpPut("items/{itemId:int}")]
    public IActionResult SetQuantity(int itemId, [FromBody] SetQuantityRequest request)
    {
        if (request.Quantity < 1)
            return BadRequest(new { error = "Quantity must be at least 1" });
        lists.SetQuantity(itemId, request.Quantity);
        return NoContent();
    }

    [HttpPost("{id:int}/refresh-prices")]
    public IActionResult RefreshPrices(int id)
    {
        lists.RefreshPrices(id);
        return NoContent();
    }

    /// <summary>Write every item of the list into <paramref name="request"/>'s location as owned lots,
    /// then delete the (now-consumed) list.</summary>
    [HttpPost("{id:int}/commit")]
    public ActionResult<CommitListResultDto> Commit(int id, [FromBody] CommitListRequest request)
    {
        if (request.ContainerId <= 0)
            return BadRequest(new { error = "A target location is required" });

        var list = FindList(id);
        if (list is null)
            return NotFound();

        var items = lists.GetItems(id);
        if (items.Count == 0)
            return BadRequest(new { error = "The list is empty" });

        var cards = items.Select(item => new CollectionCard
        {
            Game = list.Game,
            GameCardId = item.GameCardId,
            Name = item.CardName,
            SetCode = item.SetCode ?? "",
            Number = item.CollectorNumber ?? "",
            IsFoil = item.IsFoil,
            FoilType = item.IsFoil ? item.FoilType : null,
            Quantity = Math.Max(1, item.Quantity),
            Condition = string.IsNullOrWhiteSpace(request.Condition) ? "NM" : request.Condition,
            ContainerId = request.ContainerId,
            DateAdded = DateTime.UtcNow,
        }).ToList();

        var imported = binderCards.ImportCollectionCards(cards, skipDuplicates: false);
        lists.DeleteList(id);
        return new CommitListResultDto(imported, ListDeleted: true);
    }

    /// <summary>No <c>GetList(id)</c> on the service — scan the (few) games to find the owning list.</summary>
    private CardList? FindList(int id)
    {
        foreach (var game in Enum.GetValues<CardGame>())
        {
            var found = lists.GetLists(game).FirstOrDefault(l => l.Id == id);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static CardListItemDto ToDto(CardListItem i) => new(
        i.Id, i.GameCardId, i.CardName, i.SetCode, i.CollectorNumber,
        i.IsFoil, i.FoilType, i.Quantity, i.AddedMarketPrice, i.IsUnpriced);
}
