using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Collection.Lists;
using OmniCard.Web.Services;
using OmniCard.Web.Helpers;
using OmniCard.Shared.Audit;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Security;
using OmniCard.Shared.Sites;
using OmniCard.Shared.Storage;
using OmniCard.Web.Api.Infrastructure;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Saved card lists (want-lists, buy-lists, trade binders…). CRUD + item management via
/// <see cref="IListService"/>. <see cref="ListFulfillmentPlanner"/> splits a list into owned copies (exact
/// printing) and copies still to buy; that split drives the per-row owned counts, the printable pick / buy
/// lists, and fulfillment, which writes through <see cref="WebBinderCardService"/> (the web-safe write
/// path) since the desktop's <c>CommitToLocation</c> relies on a WPF-only <c>ICardService</c> method.
///
/// <para>Site rules: any owned card the user can <em>read</em> may be added to a list (the collection
/// search already only shows readable sites), and owned counts only look at readable sites. Fulfilling
/// needs <em>write</em> access to each target location's site and to the site of every owned card it
/// relocates.</para></summary>
public sealed class ListsController(
    IListService lists,
    IDecklistService decklists,
    WebBinderCardService binderCards,
    ICardService cardService,
    CardImageCacheService imageCache,
    ListFulfillmentPlanner planner,
    IDecklistPrintExporter printExporter,
    RequestSiteAccess? siteAccess = null) : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.ListsView)]
    public ActionResult<IReadOnlyList<CardListDto>> Get([FromQuery] string? game)
    {
        if (LocationsController.ParseGame(game) is not { } g)
            return BadRequest(new { error = "A game is required" });
        return lists.GetLists(g)
            .Select(l => new CardListDto(l.Id, l.Name, l.Game.ToString(), l.Notes, lists.GetItems(l.Id).Count))
            .ToList();
    }

    [HttpPost]
    [RequirePermission(Permissions.ListsCreate)]
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
    [RequirePermission(Permissions.ListsEdit)]
    public IActionResult Rename(int id, [FromBody] RenameRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required" });
        lists.RenameList(id, request.Name.Trim());
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.ListsDelete)]
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
    [RequirePermission(Permissions.ListsCreate)]
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
    [RequirePermission(Permissions.ListsView)]
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
    [RequirePermission(Permissions.ListsEdit)]
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
    [RequirePermission(Permissions.ListsEdit)]
    [RequireSiteAccess(SiteAccessLevel.Read, Lots = "LotId")]
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
    [RequirePermission(Permissions.ListsEdit)]
    public IActionResult RemoveItem(int itemId)
    {
        lists.RemoveItem(itemId);
        return NoContent();
    }

    [HttpPut("items/{itemId:int}")]
    [RequirePermission(Permissions.ListsEdit)]
    public IActionResult SetQuantity(int itemId, [FromBody] SetQuantityRequest request)
    {
        if (request.Quantity < 1)
            return BadRequest(new { error = "Quantity must be at least 1" });
        lists.SetQuantity(itemId, request.Quantity);
        return NoContent();
    }

    [HttpPost("{id:int}/refresh-prices")]
    [RequirePermission(Permissions.ListsEdit)]
    public IActionResult RefreshPrices(int id)
    {
        lists.RefreshPrices(id);
        return NoContent();
    }

    /// <summary>Printable copy of the whole list (quantities, owned counts, prices, totals).</summary>
    [HttpGet("{id:int}/print.pdf")]
    [RequirePermission(Permissions.ListsView)]
    public IActionResult PrintPdf(int id)
    {
        if (PlanList(id) is not var (list, plan))
            return NotFound();
        var lines = plan.Select(p => new ListPrintLine(p.Item.CardName, p.Item.SetCode, p.Item.CollectorNumber,
            p.Item.IsFoil, p.Item.Quantity, p.OwnedQuantity, p.Item.IsUnpriced ? null : p.Item.AddedMarketPrice)).ToList();
        var bytes = TempFile.Produce(".pdf", path => printExporter.ExportCardList(list.Name, lines, path));
        return File(bytes, "application/pdf", $"{DecklistController.SafeFileName(list.Name)}.pdf");
    }

    /// <summary>Printable pick list: the owned copies to pull for this list, grouped by location and walked in
    /// section/page/slot order, each with a tick-box.</summary>
    [HttpGet("{id:int}/pick-list.pdf")]
    [RequirePermission(Permissions.ListsView)]
    public IActionResult PickListPdf(int id)
    {
        if (PlanList(id) is not var (list, plan))
            return NotFound();
        var result = ListFulfillmentPlanner.ToCheckResult(list.Name, plan);
        var bytes = TempFile.Produce(".pdf", path => printExporter.ExportPullList(result, path, "Pick List"));
        return File(bytes, "application/pdf", $"pick-list-{DecklistController.SafeFileName(list.Name)}.pdf");
    }

    /// <summary>Printable buy list: the copies the collection doesn't cover, with prices and a tick-box.</summary>
    [HttpGet("{id:int}/buy-list.pdf")]
    [RequirePermission(Permissions.ListsView)]
    public IActionResult BuyListPdf(int id)
    {
        if (PlanList(id) is not var (list, plan))
            return NotFound();
        var result = ListFulfillmentPlanner.ToCheckResult(list.Name, plan);
        var bytes = TempFile.Produce(".pdf", path => printExporter.ExportMissingList(result, path, "Buy List"));
        return File(bytes, "application/pdf", $"buy-list-{DecklistController.SafeFileName(list.Name)}.pdf");
    }

    /// <summary>Fulfil the list: copies already in the collection (exact printing, see
    /// <see cref="ListFulfillmentPlanner"/>) move to <c>MoveToContainerId</c>, splitting a larger stack as
    /// needed, and copies the collection doesn't have are created as new lots at <c>AddToContainerId</c>.
    /// Either half can run alone; with both set it's a one-click "put this list away".
    ///
    /// <para>Done copies come off the list (an item is removed once fully done) and the list is deleted when
    /// it's empty. After a move-only run, an item's leftover is flagged awaiting purchase so the copies just
    /// moved aren't counted against it again. Both targets are checked (site write + deck-box game lock)
    /// before anything changes.</para></summary>
    [HttpPost("{id:int}/fulfill")]
    [RequirePermission(Permissions.ListsCommit)]
    [RequireSiteAccess(SiteAccessLevel.Write, Location = "MoveToContainerId,AddToContainerId")]
    public ActionResult<FulfillListResultDto> Fulfill(int id, [FromBody] FulfillListRequest request)
    {
        var moveTo = request.MoveToContainerId is > 0 ? request.MoveToContainerId : null;
        var addTo = request.AddToContainerId is > 0 ? request.AddToContainerId : null;
        if (moveTo is null && addTo is null)
            return BadRequest(new { error = "Choose a location for the cards you own, the new cards, or both" });

        var list = FindList(id);
        if (list is null)
            return NotFound();

        var items = lists.GetItems(id);
        if (items.Count == 0)
            return BadRequest(new { error = "The list is empty" });

        var plan = planner.Plan(list.Game, items, siteAccess?.Current.ReadableSiteIds, moveTo);
        var picks = moveTo is null ? [] : plan.SelectMany(p => p.Picks).ToList();
        var toAdd = addTo is null ? [] : plan.Where(p => p.MissingQuantity > 0).ToList();

        // Moving an owned copy is a write to the site it sits in now.
        if (siteAccess is not null && !siteAccess.Current.IsUnrestricted && picks.Count > 0
            && siteAccess.SitesOfLots(picks.Select(p => p.LotId)).Any(s => !siteAccess.Current.CanWrite(s)))
            return StatusCode(403, new
            {
                error = "Some cards on this list would be moved out of a site you only have read access to. " +
                        "Remove them from the list, or ask an administrator for write access to that site.",
            });

        try
        {
            if (moveTo is int m && picks.Count > 0) binderCards.ValidateDeckBoxGame(m, list.Game);
            if (addTo is int a && toAdd.Count > 0) binderCards.ValidateDeckBoxGame(a, list.Game);
        }
        catch (DeckBoxGameMismatchException ex)
        {
            return Conflict(new { error = ex.Message });
        }

        var moved = 0;
        var added = 0;
        var listDeleted = false;

        if (moveTo is int moveTarget && picks.Count > 0)
        {
            // Copies already sitting in the target are skipped by the move but still count as done.
            binderCards.MoveQuantitiesToContainer(picks.Select(p => (p.LotId, p.Quantity)).ToList(), moveTarget);
            moved = picks.Sum(p => p.Quantity);
            listDeleted = lists.ConsumeItems(id, plan
                .Where(p => p.OwnedQuantity > 0)
                .Select(p => new ListItemConsumption(p.Item.Id, p.OwnedQuantity, MarkAwaitingPurchase: true))
                .ToList());
        }

        if (addTo is int addTarget && toAdd.Count > 0)
        {
            var condition = string.IsNullOrWhiteSpace(request.Condition) ? "NM" : request.Condition;
            var cards = toAdd.Select(p => new CollectionCard
            {
                Game = list.Game,
                GameCardId = p.Item.GameCardId,
                Name = p.Item.CardName,
                SetCode = p.Item.SetCode ?? "",
                Number = p.Item.CollectorNumber ?? "",
                IsFoil = p.Item.IsFoil,
                FoilType = p.Item.IsFoil ? p.Item.FoilType : null,
                Quantity = p.MissingQuantity,
                Condition = condition,
                ContainerId = addTarget,
                DateAdded = DateTime.UtcNow,
            }).ToList();
            binderCards.ImportCollectionCards(cards, skipDuplicates: false);
            added = toAdd.Sum(p => p.MissingQuantity);
            listDeleted = lists.ConsumeItems(id, toAdd
                .Select(p => new ListItemConsumption(p.Item.Id, p.MissingQuantity))
                .ToList());
        }

        var remaining = Math.Max(0, items.Sum(i => i.Quantity) - moved - added);
        return new FulfillListResultDto(moved, added, remaining, listDeleted);
    }

    /// <summary>The list and its owned/missing split over the sites the user can read; null when the list
    /// doesn't exist.</summary>
    private (CardList List, IReadOnlyList<ListItemPlan> Plan)? PlanList(int id)
    {
        var list = FindList(id);
        if (list is null)
            return null;
        return (list, planner.Plan(list.Game, lists.GetItems(id), siteAccess?.Current.ReadableSiteIds));
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

    /// <summary>Maps list items to DTOs, enriching each with how many copies the collection already covers
    /// (exact printing over readable sites, the same split fulfillment uses) and its best display art
    /// (local cache → catalog CDN) for hover previews.</summary>
    private List<CardListItemDto> BuildItemDtos(CardGame game, IReadOnlyList<CardListItem> items)
    {
        if (items.Count == 0)
            return [];

        var ownedByItem = planner.Plan(game, items, siteAccess?.Current.ReadableSiteIds)
            .ToDictionary(p => p.Item.Id, p => p.OwnedQuantity);

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

        return items.Select(i =>
        {
            var owned = ownedByItem.GetValueOrDefault(i.Id);
            return new CardListItemDto(
                i.Id, i.GameCardId, i.CardName, i.SetCode, i.CollectorNumber,
                i.IsFoil, i.FoilType, i.Quantity, i.AddedMarketPrice, i.IsUnpriced,
                InCollection: owned > 0,
                ImageUri: imageByCardId.GetValueOrDefault(i.GameCardId),
                OwnedQuantity: owned,
                AwaitingPurchase: i.AwaitingPurchase);
        }).ToList();
    }
}
