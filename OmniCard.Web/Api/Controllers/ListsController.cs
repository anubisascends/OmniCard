using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Web.Services;
using OmniCard.Web.Helpers;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Matching;
using OmniCard.Web.Api.Infrastructure;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Saved card lists (want-lists, buy-lists, trade binders…). CRUD + item management via
/// <see cref="IListService"/>; committing a list into a location goes through
/// <see cref="WebBinderCardService"/> (the web-safe write path), since the desktop's
/// <c>CommitToLocation</c> relies on a WPF-only <c>ICardService</c> method.</summary>
public sealed class ListsController(
    IListService lists,
    IDecklistService decklists,
    WebBinderCardService binderCards,
    ICardService cardService,
    CardImageCacheService imageCache) : ApiControllerBase
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
    public ActionResult<IReadOnlyList<CardListItemDto>> Items(int id)
    {
        var list = FindList(id);
        if (list is null)
            return new List<CardListItemDto>();
        return BuildItemDtos(list.Game, lists.GetItems(id));
    }

    /// <summary>Add a single "wholly new" card (chosen from the catalog) to the list. This only records a
    /// frozen printing on the list — it never creates a lot or otherwise touches the collection.</summary>
    [HttpPost("{id:int}/items")]
    public ActionResult<CardListItemDto> AddItem(int id, [FromBody] AddListItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GameCardId))
            return BadRequest(new { error = "A card is required" });
        var list = FindList(id);
        if (list is null)
            return NotFound();

        var match = new CardMatch
        {
            GameSpecificId = request.GameCardId,
            Name = request.Name,
            SetCode = request.SetCode ?? "",
            SetName = request.SetName ?? "",
            CollectorNumber = request.CollectorNumber ?? "",
            Rarity = request.Rarity ?? "",
            ImageUri = request.ImageUri,
        };
        var item = lists.AddPrinting(id, match, request.IsFoil, request.IsFoil ? request.FoilType : null,
            Math.Max(1, request.Quantity), ListItemSource.Manual);
        return BuildItemDtos(list.Game, [item])[0];
    }

    /// <summary>Add a card the user already owns to the list by referencing an existing inventory lot.
    /// Adding does not move or mutate the lot — the reference is only consumed at commit time, where the
    /// copies are relocated to the target location instead of being duplicated.</summary>
    [HttpPost("{id:int}/items/from-collection")]
    public ActionResult<CardListItemDto> AddItemFromCollection(int id, [FromBody] AddListItemFromCollectionRequest request)
    {
        if (request.LotId <= 0)
            return BadRequest(new { error = "A collection card is required" });
        var list = FindList(id);
        if (list is null)
            return NotFound();

        var item = lists.AddOwnedLot(id, request.LotId, Math.Max(1, request.Quantity));
        return BuildItemDtos(list.Game, [item])[0];
    }

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

    /// <summary>Commit the list into <paramref name="request"/>'s location, then delete the (now-consumed)
    /// list. Items that reference a card already in the collection (added via "from collection") are
    /// <em>relocated</em> to the target location — no duplicate lot is created — while catalog/URL items are
    /// written as brand-new owned lots. An owned reference whose lot has since vanished falls back to being
    /// created new, so nothing on the list is silently dropped.</summary>
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

        var condition = string.IsNullOrWhiteSpace(request.Condition) ? "NM" : request.Condition;
        var moved = 0;
        var toCreate = new List<CollectionCard>();

        foreach (var item in items)
        {
            var quantity = Math.Max(1, item.Quantity);

            // Cards already in the collection are just moved to the chosen location (split off a larger
            // stack as needed). If the referenced lot is gone, fall through and create it fresh.
            if (item.SourceLotId is { } lotId)
            {
                var relocated = binderCards.MoveOwnedCopiesToContainer(lotId, quantity, request.ContainerId);
                if (relocated > 0)
                {
                    moved += relocated;
                    continue;
                }
            }

            toCreate.Add(new CollectionCard
            {
                Game = list.Game,
                GameCardId = item.GameCardId,
                Name = item.CardName,
                SetCode = item.SetCode ?? "",
                Number = item.CollectorNumber ?? "",
                IsFoil = item.IsFoil,
                FoilType = item.IsFoil ? item.FoilType : null,
                Quantity = quantity,
                Condition = condition,
                ContainerId = request.ContainerId,
                DateAdded = DateTime.UtcNow,
            });
        }

        var imported = toCreate.Count > 0 ? binderCards.ImportCollectionCards(toCreate, skipDuplicates: false) : 0;
        lists.DeleteList(id);
        return new CommitListResultDto(imported + moved, ListDeleted: true);
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

    /// <summary>Maps list items to DTOs, enriching each with whether the collection already owns the
    /// printing (one bulk query) and its best display art (local cache → catalog CDN) for hover previews.</summary>
    private List<CardListItemDto> BuildItemDtos(CardGame game, IReadOnlyList<CardListItem> items)
    {
        if (items.Count == 0)
            return [];

        var owned = binderCards.GetOwnedGameCardIds(game, items.Select(i => i.GameCardId));

        // Resolve art the same way the collection does: hydrate missing URIs from the catalog, then
        // prefer the locally-cached copy where one exists.
        var stubs = items
            .Select(i => new CollectionCard { Game = game, GameCardId = i.GameCardId })
            .ToList();
        CardArtHydrator.HydrateMissingImageUris(cardService, stubs);
        imageCache.PreferCached(stubs);
        var imageByCardId = stubs
            .Where(s => !string.IsNullOrEmpty(s.ImageUri))
            .GroupBy(s => s.GameCardId)
            .ToDictionary(g => g.Key, g => g.First().ImageUri);

        return items.Select(i => new CardListItemDto(
            i.Id, i.GameCardId, i.CardName, i.SetCode, i.CollectorNumber,
            i.IsFoil, i.FoilType, i.Quantity, i.AddedMarketPrice, i.IsUnpriced,
            InCollection: owned.Contains(i.GameCardId),
            ImageUri: imageByCardId.GetValueOrDefault(i.GameCardId))).ToList();
    }
}
