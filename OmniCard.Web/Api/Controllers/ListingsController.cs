using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OmniCard.Api.Contracts;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.eBay;
using OmniCard.Shared.Audit;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Ebay;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Security;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Api.Mapping;
using OmniCard.Web.Helpers;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Active for-sale listings, unlisting, the printable pick list, and pushing listings to eBay.</summary>
public sealed class ListingsController(
    IListingService listings,
    IDbContextFactory<OmniCardDbContext> dbFactory,
    IPickListPdfExporter pickListPdf,
    IEbayListingService ebayListings,
    IEbayAuthService ebayAuth,
    ICardService cards,
    IOptions<EbaySettings> ebaySettings) : ApiControllerBase
{
    /// <summary>Loads a single-card lot (with its product) as a <see cref="CollectionCard"/>, or null.
    /// The card's image is hydrated from the game catalog when the stored one is missing so the eBay
    /// listing always has a public photo URL (eBay rejects a publish with no photo — errorId 25002).</summary>
    private CollectionCard? LoadSingleCard(int lotId)
    {
        using var ctx = dbFactory.CreateDbContext();
        var lot = ctx.Lots.AsNoTracking()
            .Include(l => l.Product)
            .FirstOrDefault(l => l.Id == lotId && l.Product.Category == ProductCategory.Single);
        if (lot is null)
            return null;

        var card = CollectionCardMapper.ToDto(lot, lot.Product, 0m);
        // Resolve a public CDN image from the catalog when the lot has none stored (scanned/imported
        // cards often do). eBay fetches listing photos from public URLs, so this is what lets a
        // relist/first-list actually publish instead of failing with "Add at least 1 photo".
        CardArtHydrator.HydrateMissingImageUris(cards, [card]);
        return card;
    }

    [HttpGet]
    [RequirePermission(Permissions.SalesListingsView)]
    public ActionResult<IReadOnlyList<ActiveListingDto>> Get([FromQuery] string? game) =>
        listings.GetActiveListings(LocationsController.ParseGame(game)).Select(DtoMapping.ToDto).ToList();

    /// <summary>Full detail (including listing id and editable sale properties) for the Manage Listings screen.</summary>
    [HttpGet("details")]
    [RequirePermission(Permissions.SalesListingsView)]
    public ActionResult<IReadOnlyList<ListingDetailDto>> GetDetails([FromQuery] string? game)
    {
        var webBase = ebaySettings.Value.WebBaseUrl;
        return listings.GetListingDetails(LocationsController.ParseGame(game))
            .Select(l => DtoMapping.ToDto(l, webBase)).ToList();
    }

    /// <summary>Edit an active listing's sale properties (price, channel, quantity, note) in place.</summary>
    [HttpPut("{listingId:int}")]
    [RequirePermission(Permissions.SalesListingsEdit)]
    public IActionResult Update(int listingId, [FromBody] UpdateListingRequest req)
    {
        if (!Enum.TryParse<SalesChannel>(req.Channel, ignoreCase: true, out var channel))
            return BadRequest(new { error = $"Invalid channel '{req.Channel}'" });

        try
        {
            listings.UpdateListing(listingId, req.ListedPrice, channel, req.Quantity, req.Note);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        return NoContent();
    }

    /// <summary>List a single card lot for sale, splitting the lot when only part of a stack is listed.</summary>
    [HttpPost]
    [RequirePermission(Permissions.SalesListingsCreate)]
    public IActionResult Create([FromBody] CreateListingRequest req)
    {
        if (!Enum.TryParse<SalesChannel>(req.Channel, ignoreCase: true, out var channel))
            return BadRequest(new { error = $"Invalid channel '{req.Channel}'" });
        if (req.Quantity <= 0 || req.Price < 0)
            return BadRequest(new { error = "Quantity must be positive and price non-negative." });

        try
        {
            var listedLotId = listings.ListForSaleSplitting(req.LotId, channel, req.Price, req.Quantity, req.Note);
            if (listedLotId == 0)
                return NotFound(new { error = "Card lot not found." });
            return Ok(new { lotId = listedLotId });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // e.g. the lot is already listed for sale (possibly already picked).
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>Build a draft eBay listing for a lot — server-suggested title/description, the lot's
    /// current condition/foil, and eBay category candidates from the eBay catalog search — to prefill
    /// the eBay section of the List for Sale dialog when the user picks the eBay channel.</summary>
    [HttpGet("ebay/prepare")]
    [RequirePermission(Permissions.SalesListingsCreate)]
    public ActionResult<EbayListingDraftDto> PrepareEbay([FromQuery] int lotId)
    {
        var card = LoadSingleCard(lotId);
        if (card is null)
            return NotFound(new { error = "Card lot not found." });

        // Card singles list in eBay's "CCG Individual Cards" leaf category — the one the inventory
        // item's "Card Condition" descriptor is built for. We deliberately do NOT derive the listing
        // category from a browse search: that returns other sellers' listing categories, which are
        // often a parent node or an unrelated leaf and make publishing fail with a BadRequest.
        var categories = new List<EbayCategoryOptionDto>
        {
            new(EbayListingService.SinglesCategoryId, "Collectible Card Games — Individual Cards", null),
        };

        return new EbayListingDraftDto(
            BuildEbayTitle(card), BuildEbayDescription(card),
            card.Condition, card.IsFoil, card.Game.ToString(), categories);
    }

    /// <summary>List a lot for sale and push it to eBay as a published offer. Splits the lot like the
    /// normal list-for-sale path, lists the copies locally (channel eBay), then creates/publishes an
    /// eBay offer. The local listing is kept even if the eBay push fails — the result reports the
    /// error so the user can fix setup and retry from the Manage Listings screen.</summary>
    [HttpPost("ebay")]
    [RequirePermission(Permissions.SalesListingsCreate)]
    public async Task<ActionResult<EbayListingResultDto>> CreateEbay([FromBody] CreateEbayListingRequest req)
    {
        if (req.Quantity <= 0 || req.Price < 0)
            return BadRequest(new { error = "Quantity must be positive and price non-negative." });
        if (!ebayAuth.IsConnected)
            return BadRequest(new { error = "Connect to eBay first (Settings ▸ eBay)." });

        // eBay forbids two identical listings from the same seller (errorId 25002): multiple copies of
        // the same card must be one multi-quantity listing. If an identical item (same product +
        // condition) is already actively listed on eBay under a different lot, grow that listing's
        // quantity instead of creating a duplicate.
        var identical = FindIdenticalEbayListing(req.LotId);
        if (identical is not null)
            return await MergeIntoEbayListingAsync(req, identical.Value.TargetLotId, identical.Value.ListedPrice);

        int listedLotId;
        try
        {
            listedLotId = listings.ListForSaleSplitting(req.LotId, SalesChannel.Ebay, req.Price, req.Quantity, req.Note);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // e.g. the lot is already listed for sale (possibly already picked).
            return Conflict(new { error = ex.Message });
        }
        if (listedLotId == 0)
            return NotFound(new { error = "Card lot not found." });

        var card = LoadSingleCard(listedLotId);
        if (card is null)
            return NotFound(new { error = "Listed lot not found." });

        var options = new EbayListingOptions
        {
            Price = req.Price,
            Quantity = req.Quantity, // list all copies as one multi-quantity listing
            Condition = string.IsNullOrWhiteSpace(req.Condition) ? card.Condition : req.Condition,
            Title = string.IsNullOrWhiteSpace(req.Title) ? BuildEbayTitle(card) : req.Title.Trim(),
            Description = req.Description?.Trim() ?? "",
            ListingType = string.Equals(req.ListingType, "Auction", StringComparison.OrdinalIgnoreCase)
                ? EbayListingType.Auction
                : EbayListingType.FixedPrice,
            AuctionDuration = req.AuctionDuration,
            EbayCategoryId = string.IsNullOrWhiteSpace(req.CategoryId) ? null : req.CategoryId,
        };

        var ok = await ebayListings.CreateListingAsync(card, options);

        // The lot is listed locally regardless of the eBay outcome; read back the eBay listing row
        // the service wrote for the published item id (on success) or the error message (on failure).
        using var ctx = dbFactory.CreateDbContext();
        var ebay = ctx.EbayListings.AsNoTracking().FirstOrDefault(l => l.LotId == listedLotId);
        return new EbayListingResultDto(
            listedLotId,
            ok,
            ebay?.EbayItemId,
            ok ? null : ebay?.ErrorMessage ?? "eBay listing failed. Check the server logs for details.");
    }

    /// <summary>If a card identical to <paramref name="sourceLotId"/> (same product + condition, a
    /// different lot) is already actively listed on eBay, returns that lot and its listed price so the
    /// new copies can be folded into it as a multi-quantity listing; otherwise null.</summary>
    private (int TargetLotId, decimal ListedPrice)? FindIdenticalEbayListing(int sourceLotId)
    {
        using var ctx = dbFactory.CreateDbContext();
        var source = ctx.Lots.AsNoTracking().FirstOrDefault(l => l.Id == sourceLotId);
        if (source is null)
            return null;

        var match =
            (from e in ctx.EbayListings.AsNoTracking()
             where e.Status == EbayListingStatus.Active
             join l in ctx.Lots.AsNoTracking() on e.LotId equals l.Id
             where l.Id != sourceLotId && l.ProductId == source.ProductId && l.Condition == source.Condition
             join listing in ctx.Listings.AsNoTracking() on l.Id equals listing.LotId
             where listing.Status == ListingStatus.Listed || listing.Status == ListingStatus.Picked
             select new { l.Id, listing.ListedPrice }).FirstOrDefault();

        return match is null ? null : (match.Id, match.ListedPrice);
    }

    /// <summary>Grows an existing identical eBay listing's quantity by the requested amount. Pushes the
    /// new total to eBay first; only on success does it move the physical copies into the listed lot, so
    /// a failed push never leaves a half-merged local state (nor double-counts on retry).</summary>
    private async Task<ActionResult<EbayListingResultDto>> MergeIntoEbayListingAsync(
        CreateEbayListingRequest req, int targetLotId, decimal existingPrice)
    {
        int currentQty, sourceQty;
        using (var ctx = dbFactory.CreateDbContext())
        {
            sourceQty = ctx.Lots.AsNoTracking().Where(l => l.Id == req.LotId).Select(l => l.Quantity).FirstOrDefault();
            currentQty = ctx.Listings.AsNoTracking()
                .Where(l => l.LotId == targetLotId && (l.Status == ListingStatus.Listed || l.Status == ListingStatus.Picked))
                .Select(l => l.Quantity).FirstOrDefault();
        }
        if (req.Quantity < 1 || req.Quantity > sourceQty)
            return BadRequest(new { error = "Quantity must be between 1 and the lot's quantity." });

        var target = LoadSingleCard(targetLotId);
        if (target is null)
            return NotFound(new { error = "Existing listed lot not found." });

        var newQuantity = currentQty + req.Quantity;
        var options = new EbayListingOptions
        {
            Price = existingPrice,   // keep the live listing's price; a merge shouldn't silently reprice it
            Quantity = newQuantity,
            Condition = target.Condition,
            Title = BuildEbayTitle(target),
            Description = BuildEbayDescription(target),
            EbayCategoryId = EbayListingService.SinglesCategoryId,
        };

        var ok = await ebayListings.CreateListingAsync(target, options);
        if (!ok)
        {
            using var ctx = dbFactory.CreateDbContext();
            var failed = ctx.EbayListings.AsNoTracking().FirstOrDefault(l => l.LotId == targetLotId);
            return new EbayListingResultDto(targetLotId, false, failed?.EbayItemId,
                failed?.ErrorMessage ?? "eBay quantity update failed. Check the server logs for details.");
        }

        // eBay accepted the new quantity — now fold the copies into the listed lot locally.
        listings.MergeIntoListing(req.LotId, req.Quantity, targetLotId);

        using var read = dbFactory.CreateDbContext();
        var ebay = read.EbayListings.AsNoTracking().FirstOrDefault(l => l.LotId == targetLotId);
        return new EbayListingResultDto(targetLotId, true, ebay?.EbayItemId, null);
    }

    /// <summary>Update (revise) an already-published eBay listing: re-pushes the offer with the edited
    /// price/title/description/condition/category (creating-or-updating the existing offer and
    /// republishing) and keeps the local listing's price in sync. Requires the lot to already have an
    /// active eBay listing.</summary>
    [HttpPost("ebay/revise")]
    [RequirePermission(Permissions.SalesListingsEdit)]
    public async Task<ActionResult<EbayListingResultDto>> ReviseEbay([FromBody] ReviseEbayListingRequest req)
    {
        if (req.Price < 0)
            return BadRequest(new { error = "Price must be non-negative." });
        if (!ebayAuth.IsConnected)
            return BadRequest(new { error = "Connect to eBay first (Settings ▸ eBay)." });

        // Require an existing active eBay listing for this lot — revise is not a first-time list.
        Listing? listing;
        using (var ctx = dbFactory.CreateDbContext())
        {
            var hasActiveEbay = ctx.EbayListings.AsNoTracking()
                .Any(l => l.LotId == req.LotId && l.Status == EbayListingStatus.Active);
            if (!hasActiveEbay)
                return NotFound(new { error = "No active eBay listing found for this lot." });
            listing = ctx.Listings.AsNoTracking().FirstOrDefault(l => l.Id == req.ListingId);
        }

        var card = LoadSingleCard(req.LotId);
        if (card is null)
            return NotFound(new { error = "Card lot not found." });

        var options = new EbayListingOptions
        {
            Price = req.Price,
            Quantity = Math.Max(1, listing?.Quantity ?? 1), // preserve a multi-quantity listing's count
            Condition = string.IsNullOrWhiteSpace(req.Condition) ? card.Condition : req.Condition,
            Title = string.IsNullOrWhiteSpace(req.Title) ? BuildEbayTitle(card) : req.Title.Trim(),
            Description = req.Description?.Trim() ?? "",
            ListingType = string.Equals(req.ListingType, "Auction", StringComparison.OrdinalIgnoreCase)
                ? EbayListingType.Auction
                : EbayListingType.FixedPrice,
            AuctionDuration = req.AuctionDuration,
            EbayCategoryId = string.IsNullOrWhiteSpace(req.CategoryId) ? null : req.CategoryId,
        };

        // CreateListingAsync updates the existing offer (price/policies/category) and republishes — a
        // full revise of the live listing. It also idempotently re-lists locally (a no-op here).
        var ok = await ebayListings.CreateListingAsync(card, options);

        // Keep the local listing's price aligned with the eBay revision (preserve its qty/note).
        if (ok && listing is not null)
            listings.UpdateListing(listing.Id, req.Price, SalesChannel.Ebay, listing.Quantity, listing.Note);

        using var read = dbFactory.CreateDbContext();
        var ebay = read.EbayListings.AsNoTracking().FirstOrDefault(l => l.LotId == req.LotId);
        return new EbayListingResultDto(
            req.LotId,
            ok,
            ebay?.EbayItemId,
            ok ? null : ebay?.ErrorMessage ?? "eBay update failed. Check the server logs for details.");
    }

    // eBay listing titles are capped at 80 characters.
    private static string BuildEbayTitle(CollectionCard card)
    {
        var parts = new List<string> { card.Name };
        if (!string.IsNullOrWhiteSpace(card.SetName)) parts.Add(card.SetName);
        if (!string.IsNullOrWhiteSpace(card.Number)) parts.Add($"#{card.Number}");
        if (card.IsFoil) parts.Add("Foil");
        if (!string.IsNullOrWhiteSpace(card.Condition)) parts.Add(card.Condition);
        var title = string.Join(" ", parts);
        return title.Length <= 80 ? title : title[..80];
    }

    private static string BuildEbayDescription(CollectionCard card)
    {
        var foil = card.IsFoil
            ? (string.IsNullOrWhiteSpace(card.FoilType) ? "Foil" : card.FoilType)
            : "Non-foil";
        var set = string.IsNullOrWhiteSpace(card.SetName) ? "" : $" — {card.SetName}";
        var num = string.IsNullOrWhiteSpace(card.Number) ? "" : $" (#{card.Number})";
        var rarity = string.IsNullOrWhiteSpace(card.Rarity) ? "" : $" {card.Rarity}.";
        return $"{card.Name}{set}{num}. {foil}, condition {card.Condition}.{rarity}".Trim();
    }

    /// <summary>List several whole lots for sale at once, each at its own price.</summary>
    [HttpPost("bulk")]
    [RequirePermission(Permissions.SalesListingsCreate)]
    public IActionResult CreateBulk([FromBody] BulkListingRequest req)
    {
        if (!Enum.TryParse<SalesChannel>(req.Channel, ignoreCase: true, out var channel))
            return BadRequest(new { error = $"Invalid channel '{req.Channel}'" });

        var ids = req.Items.Select(i => i.LotId).Distinct().ToList();
        using var ctx = dbFactory.CreateDbContext();
        var quantities = ctx.Lots.Where(l => ids.Contains(l.Id)).ToDictionary(l => l.Id, l => l.Quantity);

        var listed = 0;
        foreach (var item in req.Items)
        {
            if (item.Price < 0 || !quantities.TryGetValue(item.LotId, out var qty)) continue;
            // Whole-lot listings never split; ListForSale already skips lots that are already listed.
            listed += listings.ListForSale([item.LotId], channel, item.Price, qty, req.Note) > 0 ? 1 : 0;
        }
        return Ok(new { listed });
    }

    /// <summary>Mark the given lots' active listings as picked — moves each lot to the configured
    /// for-sale location (a 400 is returned if no such location is configured).</summary>
    [HttpPost("pick")]
    [RequirePermission(Permissions.SalesListingsPick)]
    public IActionResult Pick([FromBody] LotIdsRequest req)
    {
        try
        {
            var picked = listings.MarkPicked(req.LotIds);
            return Ok(new { picked });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Cancel the active listing on a lot (returns it to not-listed). If the lot has an active
    /// eBay listing it is ended on eBay first so the item is removed from the marketplace too; the local
    /// unlist proceeds regardless of the eBay outcome.</summary>
    [HttpDelete("lot/{lotId:int}")]
    [RequirePermission(Permissions.SalesListingsDelete)]
    public async Task<IActionResult> Unlist(int lotId)
    {
        EbayListing? ebay;
        using (var ctx = dbFactory.CreateDbContext())
            ebay = ctx.EbayListings.AsNoTracking().FirstOrDefault(l => l.LotId == lotId && l.Status == EbayListingStatus.Active);

        // EndListingAsync ends the eBay item and unlists locally; still unlist below to cover lots with
        // no (or a failed) eBay listing, and so a failed eBay end doesn't leave the card listed locally.
        if (ebay is not null)
            await ebayListings.EndListingAsync(ebay);

        listings.Unlist([lotId]);
        return NoContent();
    }

    /// <summary>Printable pick list (cards to pull for active listings), optionally game-filtered.</summary>
    [HttpGet("picklist.pdf")]
    [RequirePermission(Permissions.SalesListingsView)]
    public IActionResult PickListPdf([FromQuery] string? game)
    {
        var entries = listings.GetPickList(LocationsController.ParseGame(game));
        var bytes = TempFile.Produce(".pdf", p => pickListPdf.Export(entries, p));
        return File(bytes, "application/pdf", "pick-list.pdf");
    }
}
