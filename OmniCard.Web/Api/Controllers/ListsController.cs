using System.Text;
using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Collection.Lists;
using OmniCard.Web.Services;
using OmniCard.Web.Helpers;
using OmniCard.Shared.Audit;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Security;
using OmniCard.Shared.Sites;
using OmniCard.Shared.Storage;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services.TagRules;

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
    RequestSiteAccess? siteAccess = null,
    TagRuleService? tagRules = null) : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.ListsView)]
    public ActionResult<IReadOnlyList<CardListDto>> Get([FromQuery] string? game)
    {
        if (LocationsController.ParseGame(game) is not { } g)
            return BadRequest(new { error = "A game is required" });
        return lists.GetLists(g)
            .Select(l => ToDto(l, lists.GetItems(l.Id).Count))
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
        return ToDto(created, 0);
    }

    /// <summary>Sets the list's forced card language (null/blank = any language). Owned counts, prints,
    /// fulfillment and "find in collection" all follow it.</summary>
    [HttpPut("{id:int}/language")]
    [RequirePermission(Permissions.ListsEdit)]
    public IActionResult SetLanguage(int id, [FromBody] SetListLanguageRequest request)
    {
        if (FindList(id) is null)
            return NotFound();
        if (!string.IsNullOrWhiteSpace(request.Language) && CardLanguages.Normalize(request.Language) is null)
            return BadRequest(new { error = $"Unknown language '{request.Language}'" });
        lists.SetLanguage(id, request.Language);
        return NoContent();
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
    /// (named after the deck, with the request's forced language) when <c>ListId</c> is null, otherwise
    /// appends to the existing list. The URL is remembered as the list's source for "update from URL" (an
    /// existing list keeps the source it already has). Card names are resolved to printings via
    /// <see cref="IListService.AddCardsByName"/>; any that don't resolve come back in
    /// <c>UnresolvedNames</c>.</summary>
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
            if (string.IsNullOrWhiteSpace(existing.SourceUrl))
                lists.SetSourceUrl(list.Id, request.Url);
        }
        else
        {
            if (LocationsController.ParseGame(request.Game) is not { } game)
                return BadRequest(new { error = $"Unknown game '{request.Game}'" });
            var name = string.IsNullOrWhiteSpace(deckName) ? "Imported deck" : deckName;
            if (!string.IsNullOrWhiteSpace(request.Language) && CardLanguages.Normalize(request.Language) is null)
                return BadRequest(new { error = $"Unknown language '{request.Language}'" });
            list = lists.CreateList(name, game);
            lists.SetSourceUrl(list.Id, request.Url);
            lists.SetLanguage(list.Id, request.Language);
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
        return BuildItemDtos(list, lists.GetItems(id));
    }

    /// <summary>Re-fetch the list's deck (the stored source URL, or <c>Url</c> when given) and return what
    /// would change: cards added, removed, or with a different quantity. Nothing is written — the user
    /// reviews the rows and sends the approved ones to <see cref="ApplyUpdate"/>. Added cards carry how many
    /// copies the collection already covers (forced language and readable sites apply).</summary>
    [HttpPost("{id:int}/update-preview")]
    [RequirePermission(Permissions.ListsEdit)]
    public async Task<ActionResult<ListUpdatePreviewDto>> PreviewUpdate(int id, [FromBody] ListUpdatePreviewRequest request)
    {
        var list = FindList(id);
        if (list is null)
            return NotFound();
        var url = string.IsNullOrWhiteSpace(request.Url) ? list.SourceUrl : request.Url.Trim();
        if (string.IsNullOrWhiteSpace(url))
            return BadRequest(new { error = "This list has no source URL. Enter the Moxfield / Archidekt URL to update from." });

        var fetched = await decklists.FetchDecklistAsync(url);
        if (fetched is null)
            return BadRequest(new { error = "Couldn't fetch that decklist URL. Supported: Moxfield, Archidekt." });
        var (deckName, entries) = fetched.Value;

        var preview = lists.PreviewUpdate(id, deckName, entries);

        // How much of each newly added card the collection already covers, as if it were on the list.
        var adds = preview.Rows.Where(r => r.Kind == ListUpdateKind.Add).ToList();
        var ownedByCard = new Dictionary<string, int>();
        if (adds.Count > 0)
        {
            var hypothetical = adds.Select((r, n) => new CardListItem
            {
                Id = -(n + 1), GameCardId = r.GameCardId, CardName = r.CardName, SetCode = r.SetCode,
                CollectorNumber = r.CollectorNumber, IsFoil = r.IsFoil, Quantity = r.NewQuantity,
            }).ToList();
            foreach (var plan in planner.Plan(list.Game, hypothetical, siteAccess?.Current.ReadableSiteIds, language: list.Language))
                ownedByCard[plan.Item.GameCardId] = plan.OwnedQuantity;
        }

        var rows = preview.Rows.Select(r => new ListUpdateRowDto(
            r.Kind.ToString(), r.GameCardId, r.CardName, r.SetCode, r.SetName, r.CollectorNumber, r.Rarity, r.ImageUri,
            r.IsFoil, r.OldQuantity, r.NewQuantity, r.Price, r.HandAdded,
            r.Kind == ListUpdateKind.Add ? ownedByCard.GetValueOrDefault(r.GameCardId) : 0)).ToList();
        return new ListUpdatePreviewDto(preview.DeckName, url, rows, preview.UnchangedCount, preview.UnresolvedNames);
    }

    /// <summary>Apply the update rows the user approved (each sets its printing's quantity on the list) and
    /// remember the URL they came from as the list's source.</summary>
    [HttpPost("{id:int}/update-apply")]
    [RequirePermission(Permissions.ListsEdit)]
    public IActionResult ApplyUpdate(int id, [FromBody] ListUpdateApplyRequest request)
    {
        if (FindList(id) is null)
            return NotFound();
        var rows = new List<ListUpdateRow>();
        foreach (var r in request.Rows)
        {
            if (!Enum.TryParse<ListUpdateKind>(r.Kind, ignoreCase: true, out var kind) || string.IsNullOrWhiteSpace(r.GameCardId))
                return BadRequest(new { error = "Invalid update row" });
            rows.Add(new ListUpdateRow(kind, r.GameCardId, r.CardName, r.SetCode, r.SetName, r.CollectorNumber,
                r.Rarity, r.ImageUri, r.IsFoil, r.OldQuantity, Math.Max(0, r.NewQuantity), r.Price, r.HandAdded));
        }
        lists.ApplyUpdate(id, rows);
        if (!string.IsNullOrWhiteSpace(request.Url))
            lists.SetSourceUrl(id, request.Url);
        return NoContent();
    }

    /// <summary>"Find in collection": for each card the exact-printing match leaves short, owned copies of
    /// other printings with the same name that could stand in, with suggested quantities. Read-only — the
    /// user approves stand-ins via <see cref="ApplySubstitutes"/>.</summary>
    [HttpPost("{id:int}/substitutes")]
    [RequirePermission(Permissions.ListsView)]
    public ActionResult<IReadOnlyList<ListItemSubstitutesDto>> FindSubstitutes(int id)
    {
        var list = FindList(id);
        if (list is null)
            return NotFound();
        var found = planner.FindSubstitutes(list.Game, lists.GetItems(id), siteAccess?.Current.ReadableSiteIds, list.Language);
        var images = ImagesFor(list.Game, found.SelectMany(f => f.Candidates).Select(c => c.GameCardId));
        return found.Select(f => new ListItemSubstitutesDto(
            f.Item.Id, f.Item.CardName, f.Item.SetCode, f.Item.CollectorNumber, f.Item.IsFoil, f.MissingQuantity,
            f.Candidates.Select(c => new ListSubstituteCandidateDto(
                c.LotId, c.GameCardId, c.CardName, c.SetCode, c.CollectorNumber, c.IsFoil, c.Language, c.Condition,
                c.ContainerName, c.Page, c.Slot, c.Section, c.Available, c.Suggested,
                images.GetValueOrDefault(c.GameCardId), c.IgnoredReason)).ToList())).ToList();
    }

    /// <summary>Apply approved stand-ins: each moves copies of a list card onto an owned lot of another
    /// printing (a reference only — the lot isn't moved until the list is put away). Each (card, lot) pair
    /// must be one <see cref="FindSubstitutes"/> offers as usable right now — so a lot in a site the user
    /// can't read, in a location ignored for lists, or listed for sale is rejected.</summary>
    [HttpPost("{id:int}/substitutes/apply")]
    [RequirePermission(Permissions.ListsEdit)]
    public IActionResult ApplySubstitutes(int id, [FromBody] ListSubstitutionsApplyRequest request)
    {
        var list = FindList(id);
        if (list is null)
            return NotFound();
        var subs = request.Substitutions.Where(s => s.Quantity > 0).ToList();
        if (subs.Count == 0)
            return BadRequest(new { error = "Choose at least one card to use" });

        var offered = planner.FindSubstitutes(list.Game, lists.GetItems(id), siteAccess?.Current.ReadableSiteIds, list.Language)
            .SelectMany(f => f.Candidates.Where(c => c.IgnoredReason is null).Select(c => (f.Item.Id, c.LotId)))
            .ToHashSet();
        if (subs.Any(s => !offered.Contains((s.ItemId, s.LotId))))
            return BadRequest(new { error = "Some of those copies can't be used any more (moved, listed, or in an ignored location). Check again." });
        lists.ApplySubstitutions(id, subs.Select(s => new ListSubstitution(s.ItemId, s.LotId, s.Quantity)).ToList());
        return NoContent();
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
        return BuildItemDtos(list, [item])[0];
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
        return BuildItemDtos(list, [item])[0];
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

    /// <summary>The list as a decklist text file (<c>1x Aragorn, the Uniter (LTR) 192</c>, <c>*F*</c> = foil) or
    /// a CSV (Qty, Card Name, Set, Collector Number, Foil). <c>scope</c> is <c>all</c>, <c>buy</c> (copies the
    /// collection doesn't cover) or <c>owned</c>, split exactly like the pick / buy lists.</summary>
    [HttpGet("{id:int}/export")]
    [RequirePermission(Permissions.ListsView)]
    public IActionResult Export(int id, [FromQuery] string? scope, [FromQuery] string? format)
    {
        ListExportScope? parsedScope = scope?.ToLowerInvariant() switch
        {
            null or "" or "all" => ListExportScope.All,
            "buy" => ListExportScope.ToBuy,
            "owned" => ListExportScope.Owned,
            _ => null,
        };
        if (parsedScope is not { } exportScope)
            return BadRequest(new { error = $"Unknown scope '{scope}'" });
        var csv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
        if (!csv && !string.IsNullOrEmpty(format) && !string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Unknown format '{format}'" });
        if (PlanList(id) is not var (list, plan))
            return NotFound();

        var lines = ListExportFormatter.Lines(plan, exportScope);
        var suffix = exportScope switch { ListExportScope.ToBuy => "-to-buy", ListExportScope.Owned => "-owned", _ => "" };
        var name = $"{DecklistController.SafeFileName(list.Name)}{suffix}";
        return csv
            ? File(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(ListExportFormatter.ToCsv(lines))).ToArray(),
                "text/csv; charset=utf-8", $"{name}.csv")
            : File(Encoding.UTF8.GetBytes(ListExportFormatter.ToText(lines)), "text/plain; charset=utf-8", $"{name}.txt");
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

        var plan = planner.Plan(list.Game, items, siteAccess?.Current.ReadableSiteIds, moveTo, list.Language);
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
        var ruleTagged = 0;
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
            // New copies take the list's forced language (English when it allows any), on that language's
            // catalog row when the catalog has one.
            var language = list.Language ?? CardLanguages.English;
            var languageAware = language == CardLanguages.English ? null : LanguageAware(list.Game);
            var cards = toAdd.Select(p =>
            {
                var variant = languageAware?.FindLanguageVariant(p.Item.GameCardId, language);
                return new CollectionCard
                {
                    Game = list.Game,
                    GameCardId = variant?.GameSpecificId ?? p.Item.GameCardId,
                    Name = p.Item.CardName,
                    SetCode = variant?.SetCode ?? p.Item.SetCode ?? "",
                    Number = variant?.CollectorNumber ?? p.Item.CollectorNumber ?? "",
                    ImageUri = variant?.ImageUri,
                    IsFoil = p.Item.IsFoil,
                    FoilType = p.Item.IsFoil ? p.Item.FoilType : null,
                    Language = language,
                    Quantity = p.MissingQuantity,
                    Condition = condition,
                    ContainerId = addTarget,
                    DateAdded = DateTime.UtcNow,
                };
            }).ToList();
            var lotIds = binderCards.ImportCollectionCardLots(cards, skipDuplicates: false);
            ruleTagged = tagRules?.ApplyToNewLots(lotIds) ?? 0;
            added = toAdd.Sum(p => p.MissingQuantity);
            listDeleted = lists.ConsumeItems(id, toAdd
                .Select(p => new ListItemConsumption(p.Item.Id, p.MissingQuantity))
                .ToList());
        }

        var remaining = Math.Max(0, items.Sum(i => i.Quantity) - moved - added);
        return new FulfillListResultDto(moved, added, remaining, listDeleted, ruleTagged);
    }

    /// <summary>The list and its owned/missing split over the sites the user can read; null when the list
    /// doesn't exist.</summary>
    private (CardList List, IReadOnlyList<ListItemPlan> Plan)? PlanList(int id)
    {
        var list = FindList(id);
        if (list is null)
            return null;
        return (list, planner.Plan(list.Game, lists.GetItems(id), siteAccess?.Current.ReadableSiteIds, language: list.Language));
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

    /// <summary>The game's catalog when it can serve other-language rows; null otherwise (or when the game
    /// has no service registered).</summary>
    private ICatalogLanguageAware? LanguageAware(CardGame game)
    {
        try { return cardService.GetGameService(game) as ICatalogLanguageAware; }
        catch (ArgumentException) { return null; }
    }

    private static CardListDto ToDto(CardList l, int itemCount) =>
        new(l.Id, l.Name, l.Game.ToString(), l.Notes, itemCount, l.Language, l.SourceUrl);

    /// <summary>Maps list items to DTOs, enriching each with how many copies the collection already covers
    /// (exact printing in the list's language over readable sites, the same split fulfillment uses) and its
    /// best display art (local cache → catalog CDN) for hover previews.</summary>
    private List<CardListItemDto> BuildItemDtos(CardList list, IReadOnlyList<CardListItem> items)
    {
        if (items.Count == 0)
            return [];

        var planByItem = planner.Plan(list.Game, items, siteAccess?.Current.ReadableSiteIds, language: list.Language)
            .ToDictionary(p => p.Item.Id);
        var images = ImagesFor(list.Game, items.Select(i => i.GameCardId));
        // Each printing's catalog language, so the page can flag rows not printed in the list's language.
        var languages = list.Language is null
            ? new Dictionary<string, string>()
            : LanguageAware(list.Game)?.GetCardLanguages(items.Select(i => i.GameCardId)) ?? new Dictionary<string, string>();

        return items.Select(i =>
        {
            var owned = planByItem.GetValueOrDefault(i.Id)?.OwnedQuantity ?? 0;
            return new CardListItemDto(
                i.Id, i.GameCardId, i.CardName, i.SetCode, i.CollectorNumber,
                i.IsFoil, i.FoilType, i.Quantity, i.AddedMarketPrice, i.IsUnpriced,
                InCollection: owned > 0,
                ImageUri: images.GetValueOrDefault(i.GameCardId),
                OwnedQuantity: owned,
                AwaitingPurchase: i.AwaitingPurchase,
                IsSubstitute: i.SubstituteForCardId is not null,
                IgnoredQuantity: planByItem.GetValueOrDefault(i.Id)?.IgnoredQuantity ?? 0,
                Language: languages.GetValueOrDefault(i.GameCardId));
        }).ToList();
    }

    /// <summary>Best display art per card id, resolved the way the collection does: hydrate missing URIs
    /// from the catalog, then prefer the locally-cached copy where one exists.</summary>
    private Dictionary<string, string?> ImagesFor(CardGame game, IEnumerable<string> gameCardIds)
    {
        var stubs = gameCardIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .Select(id => new CollectionCard { Game = game, GameCardId = id })
            .ToList();
        if (stubs.Count == 0)
            return [];
        CardArtHydrator.HydrateMissingImageUris(cardService, stubs);
        imageCache.PreferCached(stubs);
        return stubs
            .Where(s => !string.IsNullOrEmpty(s.ImageUri))
            .GroupBy(s => s.GameCardId)
            .ToDictionary(g => g.Key, g => g.First().ImageUri);
    }
}
