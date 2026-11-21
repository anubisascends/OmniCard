using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Collection.Inventory;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Ebay;
using OmniCard.Shared.Games;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Storage;
using OmniCard.Web.Services.Kiosk;

namespace OmniCard.Web.Services;

/// <summary>Bulk edits from the Locations page's selection mode: game + default sets, site, always-available,
/// ignore-for-lists, and delete. Every edit is all or nothing — one context, one save; when any location can't
/// take the change nothing is saved and <see cref="LocationBulkException.Failures"/> lists why. The per-location
/// rules are the single-location ones (<see cref="StorageContainerService.ApplyCardFilter"/>,
/// <see cref="StorageContainerService.RemoveContainer"/>), so the two paths can't drift apart.
///
/// <para>Delete is two-step: <see cref="PreviewDelete"/> reports everything that hangs off the locations
/// (every game's cards, sealed lots, listings, open orders, kiosk holds, list lines…) with a fingerprint
/// <see cref="BulkLocationDeletePreviewDto.Token"/>; <see cref="Delete"/> re-runs the preview and refuses unless
/// the fingerprint and the typed counts still match. Deleting the cards too is refused outright while any copy
/// is listed for sale, on an open order or held at the kiosk.</para>
///
/// <para>Site access is checked by the caller (<c>[RequireSiteAccess]</c> on every id + the target site).</para></summary>
public sealed class LocationBulkService(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    SavedViewService savedViews,
    ILogger<LocationBulkService> logger,
    IEnumerable<ICardGameService>? gameServices = null,
    KioskService? kiosk = null)
{
    private readonly IReadOnlyDictionary<CardGame, ICardGameService> _games =
        (gameServices ?? []).ToDictionary(s => s.Game);
    public enum ErrorKind { BadRequest, NotFound, Stale, Failures }

    /// <summary>A bulk edit that was refused. <see cref="ErrorKind.Failures"/> carries the per-location reasons.</summary>
    public sealed class LocationBulkException(ErrorKind kind, string message, IReadOnlyList<LocationBulkFailureDto>? failures = null)
        : Exception(message)
    {
        public ErrorKind Kind { get; } = kind;
        public IReadOnlyList<LocationBulkFailureDto> Failures { get; } = failures ?? [];
    }

    private static readonly HashSet<string> GameModes = ["keep", "any", "set"];
    private static readonly HashSet<string> SetsModes = ["keep", "replace", "add", "remove", "clear"];

    // --- edits ------------------------------------------------------------------------------------------

    /// <summary>Gives every location a game and/or changes its default sets (see
    /// <see cref="BulkLocationCardFilterRequest"/> for the modes).</summary>
    public LocationBulkResultDto SetCardFilter(BulkLocationCardFilterRequest req)
    {
        var gameMode = (req.GameMode ?? "keep").Trim().ToLowerInvariant();
        var setsMode = (req.SetsMode ?? "keep").Trim().ToLowerInvariant();
        if (!GameModes.Contains(gameMode)) throw Bad($"Unknown game mode '{req.GameMode}'.");
        if (!SetsModes.Contains(setsMode)) throw Bad($"Unknown sets mode '{req.SetsMode}'.");
        CardGame? target = null;
        if (gameMode == "set")
        {
            if (string.IsNullOrWhiteSpace(req.Game) || !Enum.TryParse<CardGame>(req.Game, ignoreCase: true, out var g))
                throw Bad($"Unknown game '{req.Game}'.");
            target = g;
        }
        var sets = (req.Sets ?? []).Select(s => (s ?? "").Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToList();
        if (setsMode is "replace" or "add" or "remove" && sets.Count == 0)
            throw Bad("Choose at least one set.");

        using var ctx = dbFactory.CreateDbContext();
        var containers = Load(ctx, req.Ids);

        // Set codes belong to one game, so adding/replacing them needs every location to end on that game.
        if (setsMode is "replace" or "add")
        {
            var resulting = containers.Select(c => gameMode switch { "keep" => c.Game, "any" => null, _ => target }).Distinct().ToList();
            if (resulting.Count != 1 || resulting[0] is null)
                throw Bad("Sets belong to one game: pick that game, or select locations of one game.");
        }

        var failures = new List<LocationBulkFailureDto>();
        var changed = 0;
        foreach (var c in containers)
        {
            var game = gameMode switch { "keep" => c.Game, "any" => null, _ => target };
            // A location changing game starts from no sets (its old ones are the old game's codes).
            var current = game == c.Game ? c.DefaultSetCodes.ToList() : new List<string>();
            var newSets = setsMode switch
            {
                "replace" => sets,
                "add" => current.Concat(sets.Where(s => !current.Contains(s))).ToList(),
                "remove" => current.Where(s => !sets.Contains(s)).ToList(),
                "clear" => new List<string>(),
                _ => current,
            };
            var before = (c.Game, c.DefaultSets, c.DeckTypeId);
            if (StorageContainerService.ApplyCardFilter(ctx, c, game, newSets) is { } error)
                failures.Add(new LocationBulkFailureDto(c.Id, c.Name, error));
            else if (before != (c.Game, c.DefaultSets, c.DeckTypeId))
                changed++;
        }
        return Save(ctx, failures, changed, containers.Count);
    }

    /// <summary>Moves every location (and its cards) to <paramref name="siteId"/>. Bulk can't move.</summary>
    public LocationBulkResultDto SetSite(IReadOnlyCollection<int> ids, int siteId)
    {
        using var ctx = dbFactory.CreateDbContext();
        if (!ctx.Sites.Any(s => s.Id == siteId)) throw Bad("Unknown site.");
        var containers = Load(ctx, ids);
        var failures = containers.Where(c => c.IsSystem)
            .Select(c => new LocationBulkFailureDto(c.Id, c.Name, "The Bulk location always stays in the default site."))
            .ToList();
        var changed = 0;
        foreach (var c in containers.Where(c => !c.IsSystem && c.SiteId != siteId))
        {
            c.SiteId = siteId;
            changed++;
        }
        return Save(ctx, failures, changed, containers.Count);
    }

    /// <summary>Turns always-available on/off. Bulk is always available on its own and is left alone.</summary>
    public LocationBulkResultDto SetAlwaysAvailable(IReadOnlyCollection<int> ids, bool value)
    {
        using var ctx = dbFactory.CreateDbContext();
        var containers = Load(ctx, ids);
        var changed = 0;
        foreach (var c in containers.Where(c => !c.IsSystem && c.AlwaysAvailable != value))
        {
            c.AlwaysAvailable = value;
            changed++;
        }
        return Save(ctx, [], changed, containers.Count);
    }

    /// <summary>Ignore (or stop ignoring) the locations when lists and decklist checks look for cards.</summary>
    public LocationBulkResultDto SetIgnoredForLists(IReadOnlyCollection<int> ids, bool value)
    {
        using var ctx = dbFactory.CreateDbContext();
        var containers = Load(ctx, ids);
        var changed = 0;
        foreach (var c in containers.Where(c => c.ExcludeFromDeckCheck != value))
        {
            c.ExcludeFromDeckCheck = value;
            changed++;
        }
        return Save(ctx, [], changed, containers.Count);
    }

    // --- delete -----------------------------------------------------------------------------------------

    /// <summary>What deleting the locations would do. Changes nothing.</summary>
    public BulkLocationDeletePreviewDto PreviewDelete(IReadOnlyCollection<int> ids, bool moveCardsToBulk)
    {
        using var ctx = dbFactory.CreateDbContext();
        return BuildPreview(ctx, Load(ctx, ids, tracked: false), moveCardsToBulk);
    }

    /// <summary>Deletes the locations once every check passes again (see the class summary). Returns how many
    /// copies went to Bulk or were deleted.</summary>
    public BulkLocationDeleteResultDto Delete(BulkLocationDeleteRequest req, string? userName)
    {
        using var ctx = dbFactory.CreateDbContext();
        using var tx = ctx.Database.BeginTransaction();
        var containers = Load(ctx, req.Ids);

        var system = containers.Where(c => c.IsSystem).ToList();
        if (system.Count > 0)
            throw new LocationBulkException(ErrorKind.Failures, "Bulk can't be deleted.",
                system.Select(c => new LocationBulkFailureDto(c.Id, c.Name, "Bulk can't be deleted.")).ToList());

        var preview = BuildPreview(ctx, containers, req.MoveCardsToBulk);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(preview.Token), Encoding.ASCII.GetBytes(req.Token ?? "")))
            throw new LocationBulkException(ErrorKind.Stale,
                "These locations changed since you reviewed them. Review the delete again.");
        if (req.ConfirmLocationCount != containers.Count)
            throw Bad($"Type the number of locations being deleted ({containers.Count}) to confirm.");

        if (!req.MoveCardsToBulk)
        {
            if (req.ConfirmCopies != preview.TotalCopies)
                throw Bad($"Type the number of cards being deleted ({preview.TotalCopies}) to confirm.");
            var busy = preview.Locations
                .Where(l => l.ListedCopies > 0 || l.OpenOrderCopies > 0 || l.HeldCopies > 0)
                .Select(l => new LocationBulkFailureDto(l.Id, l.Name,
                    "Holds cards that are listed for sale, on an open order or held at the kiosk. " +
                    "Unlist them or finish those orders first, or move the cards to Bulk instead."))
                .ToList();
            if (busy.Count > 0)
                throw new LocationBulkException(ErrorKind.Failures, "Some cards can't be deleted right now.", busy);
        }

        // Bulk is only needed when there are cards to move into it.
        if (req.MoveCardsToBulk && preview.TotalLots > 0 && !ctx.StorageContainers.Any(c => c.IsSystem))
            throw Bad("There's no Bulk location to move the cards to.");
        foreach (var c in containers)
            StorageContainerService.RemoveContainer(ctx, c, req.MoveCardsToBulk);
        ctx.SaveChanges();
        tx.Commit();

        // The location's own views cascade; defaults pointing at shared all-locations views don't.
        foreach (var c in containers)
            savedViews.RemoveLocation(c.Id);

        logger.LogWarning(
            "Bulk delete by {User}: {Count} locations ({Names}); {Copies} copies {Fate}",
            userName ?? "(unknown)", containers.Count, string.Join(", ", containers.Select(c => $"#{c.Id} {c.Name}")),
            preview.TotalCopies, req.MoveCardsToBulk ? "moved to Bulk" : "DELETED");

        return new BulkLocationDeleteResultDto(
            containers.Count,
            req.MoveCardsToBulk ? preview.TotalCopies : 0,
            req.MoveCardsToBulk ? 0 : preview.TotalCopies);
    }

    private BulkLocationDeletePreviewDto BuildPreview(OmniCardDbContext ctx, List<StorageContainer> containers, bool moveCardsToBulk)
    {
        var ids = containers.Select(c => c.Id).ToList();
        var lots = ctx.Lots.AsNoTracking()
            .Where(l => l.LocationId != null && ids.Contains(l.LocationId.Value))
            .Select(l => new
            {
                l.Id,
                LocationId = l.LocationId!.Value,
                l.Quantity,
                l.ProductId,
                Single = l.Product.Category == ProductCategory.Single,
                l.Product.Game,
                GameCardId = l.Product.GameCardId ?? "",
                l.Product.Foil,
                LastPrice = l.Product.LastMarketPrice,
            })
            .ToList();
        var singles = lots.Where(l => l.Single).ToList();
        var lotIds = singles.Select(l => l.Id).ToList();

        // Current catalog prices (as the location tiles use), else the last price stored on the product.
        var prices = new Dictionary<(CardGame, string, bool), decimal>();
        foreach (var group in singles.GroupBy(l => (l.Game, l.Foil)))
            if (_games.TryGetValue(group.Key.Game, out var game))
                foreach (var (cardId, price) in game.GetCurrentPrices(group.Select(l => l.GameCardId).Distinct(), group.Key.Foil))
                    prices.TryAdd((group.Key.Game, cardId, group.Key.Foil), price);
        // Copies something else is counting on, per lot.
        var listed = ctx.Listings.AsNoTracking()
            .Where(l => lotIds.Contains(l.LotId) && (l.Status == ListingStatus.Listed || l.Status == ListingStatus.Picked))
            .GroupBy(l => l.LotId).Select(g => new { LotId = g.Key, Qty = g.Sum(l => l.Quantity) })
            .ToDictionary(x => x.LotId, x => x.Qty);
        var onEbay = ctx.EbayListings.AsNoTracking()
            .Where(e => lotIds.Contains(e.LotId) && e.Status == EbayListingStatus.Active)
            .Select(e => e.LotId).ToHashSet();
        var ordered = (from line in ctx.OrderLines.AsNoTracking()
                       join order in ctx.Orders.AsNoTracking() on line.OrderId equals order.Id
                       where line.LotId != null && lotIds.Contains(line.LotId.Value)
                             && (order.Status == OrderStatus.Created || order.Status == OrderStatus.Packed)
                       group line by line.LotId!.Value into g
                       select new { LotId = g.Key, Qty = g.Sum(l => l.Quantity) })
            .ToDictionary(x => x.LotId, x => x.Qty);
        var held = kiosk?.HeldCopies(ctx) ?? [];
        var listRefs = ctx.CardListItems.AsNoTracking()
            .Where(i => i.SourceLotId != null && lotIds.Contains(i.SourceLotId.Value))
            .GroupBy(i => i.SourceLotId!.Value).Select(g => new { LotId = g.Key, Count = g.Count() })
            .ToDictionary(x => x.LotId, x => x.Count);
        var dividers = ctx.BinderDividers.AsNoTracking().Where(d => ids.Contains(d.ContainerId))
            .GroupBy(d => d.ContainerId).Select(g => new { g.Key, Count = g.Count() }).ToDictionary(x => x.Key, x => x.Count);
        var reports = ctx.DeckReturnReports.AsNoTracking().Where(r => ids.Contains(r.DeckBoxId))
            .GroupBy(r => r.DeckBoxId).Select(g => new { g.Key, Count = g.Count() }).ToDictionary(x => x.Key, x => x.Count);
        var siteNames = ctx.Sites.AsNoTracking().ToDictionary(s => s.Id, s => s.Name);

        var rows = containers.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c =>
        {
            var mine = singles.Where(l => l.LocationId == c.Id).ToList();
            return new BulkDeleteLocationDto
            {
                Id = c.Id,
                Name = c.Name,
                Type = c.ContainerType.ToString(),
                SiteName = siteNames.GetValueOrDefault(c.SiteId),
                Game = c.Game?.ToString(),
                Lots = mine.Count,
                Copies = mine.Sum(l => l.Quantity),
                MarketValue = mine.Sum(l =>
                    (prices.TryGetValue((l.Game, l.GameCardId, l.Foil), out var price) ? price : l.LastPrice ?? 0m) * l.Quantity),
                SealedLots = lots.Count(l => !l.Single && l.LocationId == c.Id),
                ListedCopies = mine.Sum(l => Math.Max(listed.GetValueOrDefault(l.Id), onEbay.Contains(l.Id) ? 1 : 0)),
                OpenOrderCopies = mine.Sum(l => ordered.GetValueOrDefault(l.Id)),
                HeldCopies = mine.Sum(l => held.GetValueOrDefault(l.Id)),
                ListReferences = mine.Sum(l => listRefs.GetValueOrDefault(l.Id)),
                DeckLinked = !string.IsNullOrWhiteSpace(c.SourceUrl),
                Dividers = dividers.GetValueOrDefault(c.Id),
                PutBackReports = reports.GetValueOrDefault(c.Id),
            };
        }).ToList();

        // The fingerprint covers the choice, each location and every lot in it (id, product, quantity) plus
        // whatever is counting on those copies, so a delete reviewed against a different state is refused.
        var sig = new StringBuilder(moveCardsToBulk ? "bulk" : "delete");
        foreach (var c in containers.OrderBy(c => c.Id))
            sig.Append($"|{c.Id}:{c.Name}:{c.ContainerType}:{c.SiteId}");
        foreach (var l in lots.OrderBy(l => l.Id))
            sig.Append($"|{l.Id}:{l.LocationId}:{l.ProductId}:{l.Quantity}");
        foreach (var r in rows)
            sig.Append($"|{r.Id}:{r.ListedCopies}:{r.OpenOrderCopies}:{r.HeldCopies}:{r.ListReferences}");
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sig.ToString())));

        return new BulkLocationDeletePreviewDto
        {
            MoveCardsToBulk = moveCardsToBulk,
            Locations = rows,
            TotalLots = rows.Sum(r => r.Lots),
            TotalCopies = rows.Sum(r => r.Copies),
            TotalMarketValue = rows.Sum(r => r.MarketValue),
            TotalSealedLots = rows.Sum(r => r.SealedLots),
            TotalListedCopies = rows.Sum(r => r.ListedCopies),
            TotalOpenOrderCopies = rows.Sum(r => r.OpenOrderCopies),
            TotalHeldCopies = rows.Sum(r => r.HeldCopies),
            TotalListReferences = rows.Sum(r => r.ListReferences),
            Token = token,
        };
    }

    // --- helpers ----------------------------------------------------------------------------------------

    /// <summary>The distinct locations; 400 for none, 404 when any id doesn't exist (nothing half-applies to a
    /// list the user didn't mean).</summary>
    private static List<StorageContainer> Load(OmniCardDbContext ctx, IReadOnlyCollection<int>? ids, bool tracked = true)
    {
        var distinct = (ids ?? []).Distinct().ToList();
        if (distinct.Count == 0) throw Bad("Choose at least one location.");
        var query = tracked ? ctx.StorageContainers : ctx.StorageContainers.AsNoTracking();
        var found = query.Where(c => distinct.Contains(c.Id)).ToList();
        if (found.Count != distinct.Count)
            throw new LocationBulkException(ErrorKind.NotFound, "One or more locations no longer exist. Refresh and try again.");
        return found;
    }

    private static LocationBulkResultDto Save(OmniCardDbContext ctx, List<LocationBulkFailureDto> failures, int changed, int total)
    {
        if (failures.Count > 0)
            throw new LocationBulkException(ErrorKind.Failures,
                failures.Count == 1 ? "1 location can't take this change, so nothing was saved."
                    : $"{failures.Count} locations can't take this change, so nothing was saved.",
                failures);
        ctx.SaveChanges();
        return new LocationBulkResultDto(changed, total - changed);
    }

    private static LocationBulkException Bad(string message) => new(ErrorKind.BadRequest, message);
}
