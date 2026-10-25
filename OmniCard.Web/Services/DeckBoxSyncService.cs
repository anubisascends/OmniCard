using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Collection.Lists;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Storage;
using OmniCard.Shared.Tags;

namespace OmniCard.Web.Services;

/// <summary>A deck box's link to an online decklist (Moxfield / Archidekt) and "Sync from URL": fetch the
/// deck, diff it against the box by card name, and apply the user's choices. Cards the deck wants are pulled
/// from elsewhere in the collection (suggested copies, or any copy the user finds by search); cards the user
/// doesn't own are remembered as "not in collection"; cards the deck dropped are moved out, kept as sideboard,
/// or left alone. Only copies in sites the user can write are offered, since pulling one moves it.</summary>
public sealed class DeckBoxSyncService(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    IDecklistService decklists,
    ICardService cardService,
    WebBinderCardService binderCards,
    ITagService tags,
    ILogger<DeckBoxSyncService> logger)
{
    public const string SideboardTag = DeckCardClassifier.SideboardTag;

    /// <summary>A request the user can fix (bad URL, unlinked box, stale choice…). <see cref="NotFound"/>
    /// means the deck box itself doesn't exist.</summary>
    public sealed class DeckSyncException(string message, bool notFound = false) : Exception(message)
    {
        public bool NotFound { get; } = notFound;
    }

    private sealed record Box(int Id, string Name, CardGame? Game, string? SourceUrl);

    private sealed record OwnedLot(
        int LotId, string Name, string? SetCode, string? CollectorNumber, bool IsFoil, int Quantity,
        string? Language, string? Condition, string? ImageUri, int ContainerId, string ContainerName,
        ContainerType ContainerType, bool IgnoredLocation, string? Section, int? Page, int? Slot);

    // --- link ---

    /// <summary>Links the deck box to <paramref name="url"/>, or unlinks it when the URL is blank. Changing
    /// or removing the link clears the box's "not in collection" list, which belonged to the old deck.</summary>
    public void SetLink(int deckBoxId, string? url)
    {
        using var ctx = dbFactory.CreateDbContext();
        var box = ctx.StorageContainers.FirstOrDefault(c => c.Id == deckBoxId)
            ?? throw new DeckSyncException("That location no longer exists.", notFound: true);
        if (box.ContainerType != ContainerType.DeckBox)
            throw new DeckSyncException("Only deck boxes can be linked to a deck URL.");

        var trimmed = url?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            box.SourceUrl = null;
        }
        else
        {
            ValidateUrl(trimmed, box.Game, box.Name);
            if (string.Equals(box.SourceUrl, trimmed, StringComparison.Ordinal))
                return;
            box.SourceUrl = trimmed;
        }

        box.SourceSyncedAt = null;
        ctx.DeckBoxMissingCards.RemoveRange(ctx.DeckBoxMissingCards.Where(m => m.ContainerId == deckBoxId));
        ctx.SaveChanges();
    }

    /// <summary>After a deck URL was imported into <paramref name="containerId"/>: links it to that URL when
    /// it's a deck box with no link yet whose game the site serves. Returns true when it linked.</summary>
    public bool LinkIfUnlinked(int containerId, string url)
    {
        try
        {
            using var ctx = dbFactory.CreateDbContext();
            var box = ctx.StorageContainers.FirstOrDefault(c => c.Id == containerId);
            if (box is null || box.ContainerType != ContainerType.DeckBox || box.SourceUrl is not null)
                return false;
            var trimmed = url.Trim();
            if (trimmed.Length > StorageContainer.MaxSourceUrlLength
                || DecklistService.ParseUrl(trimmed).Source is not { } source
                || box.Game is not { } game || !DecklistService.SourceServesGame(source, game))
                return false;
            box.SourceUrl = trimmed;
            box.SourceSyncedAt = DateTime.UtcNow;
            ctx.SaveChanges();
            return true;
        }
        catch (DbUpdateException ex)
        {
            // The import itself succeeded; a failed link mustn't turn it into an error.
            logger.LogWarning(ex, "Couldn't link deck box {Id} to {Url}", containerId, url);
            return false;
        }
    }

    private static void ValidateUrl(string url, CardGame? game, string boxName)
    {
        if (url.Length > StorageContainer.MaxSourceUrlLength)
            throw new DeckSyncException($"That URL is too long (over {StorageContainer.MaxSourceUrlLength} characters).");
        if (DecklistService.ParseUrl(url).Source is not { } source)
            throw new DeckSyncException("That isn't a Moxfield or Archidekt deck URL — it should look like https://moxfield.com/decks/… or https://archidekt.com/decks/….");
        if (game is not { } g)
            throw new DeckSyncException($"Assign \"{boxName}\" a game before linking it to a deck.");
        if (!DecklistService.SourceServesGame(source, g))
            throw new DeckSyncException($"{source} decks are Magic: The Gathering decks, but \"{boxName}\" is a {GameName(g)} deck box.");
    }

    // --- preview ---

    /// <summary>Fetches the linked deck and diffs it against the box. Keep/cut is decided by card name (a Sol
    /// Ring is a Sol Ring whatever the printing); the deck's printing only orders the suggested copies.</summary>
    public async Task<DeckSyncPreviewDto> PreviewAsync(int deckBoxId, SiteAccess access)
    {
        var box = LoadLinkedBox(deckBoxId);
        var game = box.Game!.Value;

        var fetched = await decklists.FetchDecklistAsync(box.SourceUrl!)
            ?? throw new DeckSyncException("Couldn't download the linked deck — check the link, make sure the deck is public (private decks can't be read), and try again.");
        var (deckName, entries) = fetched;
        if (entries.Count == 0)
            throw new DeckSyncException($"\"{deckName}\" has no cards.");

        return BuildPlan(box, game, deckName, entries, access);
    }

    private DeckSyncPreviewDto BuildPlan(Box box, CardGame game, string deckName, List<DecklistEntry> entries, SiteAccess access)
    {
        using var ctx = dbFactory.CreateDbContext();

        // The deck, by card name. The first line of a name supplies the printing shown and preferred.
        var wanted = new Dictionary<string, (DecklistEntry Entry, int Quantity)>();
        foreach (var entry in entries)
        {
            var key = Normalize(entry.CardName);
            wanted[key] = wanted.TryGetValue(key, out var w) ? (w.Entry, w.Quantity + entry.Quantity) : (entry, entry.Quantity);
        }

        var inBox = LoadOwned(ctx, game, inBox: box.Id, names: null, access);
        var sideboard = tags.GetTagsByLots(inBox.Select(l => l.LotId))
            .Where(kv => kv.Value.Contains(SideboardTag, StringComparer.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToHashSet();
        // Box lots by deck name they answer to (full name, Riftbound's " - " spelling, a DFC's front face).
        var boxByName = new Dictionary<string, List<OwnedLot>>();
        foreach (var lot in inBox)
        {
            var key = wanted.Keys.FirstOrDefault(k => NameMatches(k, lot.Name)) ?? Normalize(lot.Name);
            (boxByName.TryGetValue(key, out var list) ? list : boxByName[key] = []).Add(lot);
        }

        var names = wanted.Values.SelectMany(w => NameVariants(w.Entry.CardName)).Distinct().ToList();
        var elsewhere = LoadOwned(ctx, game, inBox: null, names, access).Where(l => l.ContainerId != box.Id).ToList();
        var listed = ListedLotIds(ctx, elsewhere.Select(l => l.LotId));
        var marked = ctx.DeckBoxMissingCards.AsNoTracking()
            .Where(m => m.ContainerId == box.Id)
            .Select(m => m.CardName)
            .ToList()
            .Select(Normalize)
            .ToHashSet();

        var gameService = GameService(game);
        var suggestLeft = elsewhere.ToDictionary(l => l.LotId, l => l.Quantity);
        var adds = new List<DeckSyncAddDto>();
        var cuts = new List<DeckSyncCutDto>();
        var keepCount = 0;

        foreach (var (key, (entry, needed)) in wanted.OrderBy(w => w.Value.Entry.CardName, StringComparer.OrdinalIgnoreCase))
        {
            var held = boxByName.GetValueOrDefault(key) ?? [];
            var have = held.Sum(l => l.Quantity);
            keepCount += Math.Min(needed, have);

            // Surplus copies of a card the deck still plays: cut the ones least like the deck's printing.
            if (have > needed)
                cuts.AddRange(TakeCuts(held.OrderBy(l => IsExact(l, entry)).ThenByDescending(l => l.LotId), have - needed, sideboard));
            if (have >= needed)
                continue;

            var shortBy = needed - have;
            var sources = new List<DeckSyncSourceDto>();
            var ordered = elsewhere
                .Where(l => NameMatches(key, l.Name))
                .Select(l => (Lot: l, Ignored: listed.Contains(l.LotId) ? "Listed" : l.IgnoredLocation ? "Location" : null))
                .OrderBy(x => x.Ignored switch { null => 0, "Location" => 1, _ => 2 })
                .ThenByDescending(x => IsExact(x.Lot, entry))
                .ThenByDescending(x => x.Lot.IsFoil == (entry.Finish is not null))
                .ThenBy(x => x.Lot.ContainerType == ContainerType.DeckBox)
                .ThenBy(x => x.Lot.ContainerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Lot.Section ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Lot.Page ?? int.MaxValue)
                .ThenBy(x => x.Lot.Slot ?? int.MaxValue);
            var toSuggest = shortBy;
            foreach (var (lot, ignored) in ordered)
            {
                var suggest = ignored is null ? Math.Min(toSuggest, suggestLeft[lot.LotId]) : 0;
                suggestLeft[lot.LotId] -= suggest;
                toSuggest -= suggest;
                sources.Add(ToSource(lot, suggest, IsExact(lot, entry), ignored));
            }

            adds.Add(new DeckSyncAddDto(entry.CardName, entry.SetCode, entry.CollectorNumber, entry.Finish is not null,
                shortBy, ResolveImage(gameService, entry), marked.Contains(key), sources));
        }

        // Cards the deck doesn't play at all.
        foreach (var (key, held) in boxByName.Where(b => !wanted.ContainsKey(b.Key)).OrderBy(b => b.Value[0].Name, StringComparer.OrdinalIgnoreCase))
            cuts.AddRange(TakeCuts(held, held.Sum(l => l.Quantity), sideboard));

        return new DeckSyncPreviewDto(box.Id, box.Name, box.SourceUrl!, deckName, entries.Sum(e => e.Quantity), keepCount, adds, cuts);
    }

    private static IEnumerable<DeckSyncCutDto> TakeCuts(IEnumerable<OwnedLot> lots, int quantity, HashSet<int> sideboard)
    {
        foreach (var lot in lots)
        {
            if (quantity <= 0) yield break;
            var take = Math.Min(lot.Quantity, quantity);
            quantity -= take;
            yield return new DeckSyncCutDto(lot.LotId, lot.Name, lot.SetCode, lot.CollectorNumber, lot.IsFoil, take,
                sideboard.Contains(lot.LotId), lot.ImageUri);
        }
    }

    // --- find a copy ---

    /// <summary>The "find a copy" search: owned copies of the deck box's game whose name contains
    /// <paramref name="query"/>, outside the box, in sites the user can write. Listed copies are flagged.</summary>
    public IReadOnlyList<DeckSyncSourceDto> Search(int deckBoxId, string? query, SiteAccess access, int take = 50)
    {
        var q = query?.Trim() ?? "";
        if (q.Length < 2) return [];
        var box = LoadBox(deckBoxId);
        if (box.Game is not { } game) return [];

        using var ctx = dbFactory.CreateDbContext();
        var like = "%" + q.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[") + "%";
        var lots = QueryOwned(ctx, game, WritableSiteIds(ctx, access))
            .Where(x => x.Lot.LocationId != deckBoxId && EF.Functions.Like(x.Product.Name, like, @"\"))
            .OrderBy(x => x.Product.Name)
            .ThenBy(x => x.Container.Name)
            .Take(take)
            .AsEnumerable()
            .Select(ToOwned)
            .ToList();
        var listed = ListedLotIds(ctx, lots.Select(l => l.LotId));
        return lots
            .Select(l => ToSource(l, 0, false, listed.Contains(l.LotId) ? "Listed" : l.IgnoredLocation ? "Location" : null))
            .ToList();
    }

    // --- not in collection ---

    public IReadOnlyList<DeckBoxMissingCardDto> GetNotInCollection(int deckBoxId)
    {
        using var ctx = dbFactory.CreateDbContext();
        return ctx.DeckBoxMissingCards.AsNoTracking()
            .Where(m => m.ContainerId == deckBoxId)
            .OrderBy(m => m.CardName)
            .Select(m => new DeckBoxMissingCardDto(m.CardName, m.SetCode, m.CollectorNumber, m.Quantity))
            .ToList();
    }

    /// <summary>Drops one card from the box's "not in collection" list (the user got a copy some other way).</summary>
    public void RemoveNotInCollection(int deckBoxId, string cardName)
    {
        using var ctx = dbFactory.CreateDbContext();
        ctx.DeckBoxMissingCards.RemoveRange(ctx.DeckBoxMissingCards
            .Where(m => m.ContainerId == deckBoxId && m.CardName == cardName));
        ctx.SaveChanges();
    }

    // --- apply ---

    /// <summary>Applies the user's sync choices. Everything is validated before anything is written: picked
    /// copies must be of the box's game, outside it, in writable sites and not listed for sale; cut lots must
    /// be in the box; move destinations must be writable. Then copies move in (splitting stacks), cut copies
    /// move out or get the sideboard tag, and the "not in collection" list is replaced.</summary>
    public DeckSyncResultDto Apply(int deckBoxId, DeckSyncApplyRequest request, SiteAccess access)
    {
        var box = LoadLinkedBox(deckBoxId);
        var game = box.Game!.Value;

        var picks = request.Picks
            .Where(p => p.Quantity > 0)
            .GroupBy(p => p.LotId)
            .Select(g => (LotId: g.Key, Quantity: g.Sum(p => p.Quantity)))
            .ToList();
        var cuts = request.Cuts.Where(c => c.Quantity > 0 && !IsAction(c.Action, "Keep")).ToList();

        using (var ctx = dbFactory.CreateDbContext())
        {
            var pickIds = picks.Select(p => p.LotId).ToList();
            var pickLots = ctx.Lots.AsNoTracking().Include(l => l.Product)
                .Where(l => pickIds.Contains(l.Id))
                .ToDictionary(l => l.Id);
            var listed = ListedLotIds(ctx, pickIds);
            foreach (var (lotId, quantity) in picks)
            {
                if (!pickLots.TryGetValue(lotId, out var lot) || lot.IsMissing || lot.IsTraded
                    || lot.Product.Category != ProductCategory.Single)
                    throw new DeckSyncException("One of the chosen copies is no longer in your collection. Sync again to refresh the list.");
                if (lot.Product.Game != game)
                    throw new DeckSyncException($"{lot.Product.Name} isn't a {GameName(game)} card.");
                if (lot.LocationId == deckBoxId)
                    throw new DeckSyncException($"{lot.Product.Name} is already in \"{box.Name}\".");
                if (lot.LocationId is not int from || !access.CanWrite(SiteOf(ctx, from)))
                    throw new DeckSyncException($"You can't move {lot.Product.Name} out of its location.");
                if (listed.Contains(lotId))
                    throw new DeckSyncException($"{lot.Product.Name} is listed for sale — unlist it before putting it in a deck.");
                if (quantity > lot.Quantity)
                    throw new DeckSyncException($"Only {lot.Quantity} cop{(lot.Quantity == 1 ? "y" : "ies")} of {lot.Product.Name} are in that spot. Sync again to refresh the list.");
            }

            var cutIds = cuts.Select(c => c.LotId).Distinct().ToList();
            var cutLots = ctx.Lots.AsNoTracking().Where(l => cutIds.Contains(l.Id)).ToDictionary(l => l.Id);
            foreach (var cut in cuts)
            {
                if (!cutLots.TryGetValue(cut.LotId, out var lot) || lot.LocationId != deckBoxId)
                    throw new DeckSyncException("One of the cards to remove is no longer in the deck box. Sync again to refresh the list.");
                if (cut.Quantity > lot.Quantity)
                    throw new DeckSyncException("A card to remove has fewer copies in the deck box than chosen. Sync again to refresh the list.");
                if (IsAction(cut.Action, "Move"))
                {
                    if (cut.DestinationId is not int dest || dest == deckBoxId
                        || ctx.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == dest) is not { } destination)
                        throw new DeckSyncException("Choose where the removed cards should go.");
                    if (!access.CanWrite(destination.SiteId))
                        throw new DeckSyncException($"You can't move cards into \"{destination.Name}\".");
                    if (destination.ContainerType == ContainerType.DeckBox && destination.Game is { } destGame && destGame != game)
                        throw new DeckSyncException($"\"{destination.Name}\" only holds {GameName(destGame)} cards.");
                }
                else if (!IsAction(cut.Action, "Sideboard"))
                    throw new DeckSyncException($"Unknown action '{cut.Action}' for a removed card.");
            }
        }

        var added = picks.Count > 0 ? binderCards.MoveQuantitiesToContainer(picks, deckBoxId) : 0;

        var removed = 0;
        foreach (var group in cuts.Where(c => IsAction(c.Action, "Move")).GroupBy(c => c.DestinationId!.Value))
            removed += binderCards.MoveQuantitiesToContainer(group.Select(c => (c.LotId, c.Quantity)).ToList(), group.Key);

        var sideboarded = 0;
        foreach (var cut in cuts.Where(c => IsAction(c.Action, "Sideboard")))
        {
            // Tag only the cut copies: split them off a bigger stack first.
            var lotId = cut.LotId;
            using (var ctx = dbFactory.CreateDbContext())
            {
                var have = ctx.Lots.AsNoTracking().Where(l => l.Id == lotId).Select(l => l.Quantity).FirstOrDefault();
                if (cut.Quantity < have)
                    lotId = binderCards.SplitStack(lotId, cut.Quantity);
            }
            tags.AddTagToLots([lotId], SideboardTag);
            sideboarded += cut.Quantity;
        }

        var notInCollection = SaveSyncState(deckBoxId, request.NotInCollection);
        logger.LogInformation("Synced deck box {Box} from {Url}: {Added} added, {Removed} removed, {Sideboarded} sideboarded, {Missing} not in collection",
            box.Name, box.SourceUrl, added, removed, sideboarded, notInCollection);
        return new DeckSyncResultDto(added, removed, sideboarded, notInCollection);
    }

    /// <summary>Replaces the box's "not in collection" list and stamps the sync time. Returns the copies listed.</summary>
    private int SaveSyncState(int deckBoxId, IReadOnlyList<DeckBoxMissingCardDto> notInCollection)
    {
        using var ctx = dbFactory.CreateDbContext();
        ctx.DeckBoxMissingCards.RemoveRange(ctx.DeckBoxMissingCards.Where(m => m.ContainerId == deckBoxId));
        ctx.SaveChanges(); // before re-adding: the same card names come back under a unique index
        var rows = notInCollection
            .Where(m => !string.IsNullOrWhiteSpace(m.CardName) && m.Quantity > 0)
            .GroupBy(m => m.CardName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new DeckBoxMissingCard
            {
                ContainerId = deckBoxId,
                CardName = Truncate(g.Key, DeckBoxMissingCard.MaxCardNameLength)!,
                SetCode = Truncate(g.First().SetCode, 32),
                CollectorNumber = Truncate(g.First().CollectorNumber, 32),
                Quantity = g.Sum(m => m.Quantity),
            })
            .ToList();
        ctx.DeckBoxMissingCards.AddRange(rows);
        var box = ctx.StorageContainers.First(c => c.Id == deckBoxId);
        box.SourceSyncedAt = DateTime.UtcNow;
        ctx.SaveChanges();
        return rows.Sum(r => r.Quantity);
    }

    // --- helpers ---

    private Box LoadBox(int deckBoxId)
    {
        using var ctx = dbFactory.CreateDbContext();
        var box = ctx.StorageContainers.AsNoTracking()
            .Where(c => c.Id == deckBoxId)
            .Select(c => new { c.Id, c.Name, c.ContainerType, c.Game, c.SourceUrl })
            .FirstOrDefault()
            ?? throw new DeckSyncException("That location no longer exists.", notFound: true);
        if (box.ContainerType != ContainerType.DeckBox)
            throw new DeckSyncException("Only deck boxes can be synced from a deck URL.");
        return new Box(box.Id, box.Name, box.Game, box.SourceUrl);
    }

    private Box LoadLinkedBox(int deckBoxId)
    {
        var box = LoadBox(deckBoxId);
        if (string.IsNullOrWhiteSpace(box.SourceUrl))
            throw new DeckSyncException($"\"{box.Name}\" isn't linked to a deck URL yet.");
        if (box.Game is null)
            throw new DeckSyncException($"Assign \"{box.Name}\" a game before syncing it.");
        return box;
    }

    // Member-init (not a positional record) so EF can keep filtering through it.
    private sealed class OwnedRow
    {
        public required InventoryLot Lot { get; init; }
        public required Product Product { get; init; }
        public required StorageContainer Container { get; init; }
    }

    /// <summary>Owned singles of <paramref name="game"/> in a location, limited to <paramref name="siteIds"/>
    /// (null = every site).</summary>
    private static IQueryable<OwnedRow> QueryOwned(OmniCardDbContext ctx, CardGame game, List<int>? siteIds)
    {
        var sites = siteIds;
        return from l in ctx.Lots.AsNoTracking()
               join p in ctx.Products.AsNoTracking() on l.ProductId equals p.Id
               join sc in ctx.StorageContainers.AsNoTracking() on l.LocationId equals sc.Id
               where p.Category == ProductCategory.Single && p.Game == game && !l.IsMissing && !l.IsTraded
               where sites == null || sites.Contains(sc.SiteId)
               select new OwnedRow { Lot = l, Product = p, Container = sc };
    }

    /// <summary>Owned copies either in the box (<paramref name="inBox"/>) or, with <paramref name="names"/>,
    /// of those card names anywhere the user can write.</summary>
    private static List<OwnedLot> LoadOwned(OmniCardDbContext ctx, CardGame game, int? inBox, List<string>? names, SiteAccess access)
    {
        if (inBox is int boxId)
            return QueryOwned(ctx, game, siteIds: null)
                .Where(x => x.Lot.LocationId == boxId)
                .AsEnumerable().Select(ToOwned).ToList();

        return QueryOwned(ctx, game, WritableSiteIds(ctx, access))
            .Where(x => names!.Contains(x.Product.Name))
            .AsEnumerable().Select(ToOwned).ToList();
    }

    /// <summary>The sites the user may move cards out of (null = all of them).</summary>
    private static List<int>? WritableSiteIds(OmniCardDbContext ctx, SiteAccess access) =>
        access.IsUnrestricted ? null
        : ctx.Sites.AsNoTracking().Select(s => s.Id).AsEnumerable().Where(access.CanWrite).ToList();

    private static OwnedLot ToOwned(OwnedRow x) => new(
        x.Lot.Id, x.Product.Name, x.Product.SetCode, x.Product.CollectorNumber, x.Product.Foil, Math.Max(1, x.Lot.Quantity),
        x.Lot.Language, x.Lot.Condition, x.Product.ImageUri, x.Container.Id, x.Container.Name, x.Container.ContainerType,
        x.Container.ExcludeFromDeckCheck, x.Lot.Section, x.Lot.Page, x.Lot.Slot);

    private static DeckSyncSourceDto ToSource(OwnedLot l, int suggested, bool exact, string? ignored) => new(
        l.LotId, l.Name, l.SetCode, l.CollectorNumber, l.IsFoil, CardLanguages.Normalize(l.Language) ?? CardLanguages.English,
        l.Condition, l.ContainerId, l.ContainerName, l.Section, l.Page, l.Slot, l.Quantity, suggested, exact, ignored, l.ImageUri);

    private static int SiteOf(OmniCardDbContext ctx, int containerId) =>
        ctx.StorageContainers.AsNoTracking().Where(c => c.Id == containerId).Select(c => c.SiteId).FirstOrDefault();

    private static HashSet<int> ListedLotIds(OmniCardDbContext ctx, IEnumerable<int> lotIds)
    {
        var ids = lotIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return ctx.Listings.AsNoTracking()
            .Where(l => ids.Contains(l.LotId) && (l.Status == ListingStatus.Listed || l.Status == ListingStatus.Picked))
            .Select(l => l.LotId)
            .ToHashSet();
    }

    private static bool IsExact(OwnedLot lot, DecklistEntry entry) =>
        entry.SetCode is not null
        && string.Equals(lot.SetCode, entry.SetCode, StringComparison.OrdinalIgnoreCase)
        && (entry.CollectorNumber is null || string.Equals(lot.CollectorNumber, entry.CollectorNumber, StringComparison.OrdinalIgnoreCase));

    private static bool IsAction(string? action, string expected) => string.Equals(action, expected, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();

    /// <summary>The deck's spelling of a name plus the catalog spellings it may be stored under: Riftbound's
    /// " - " for ", ", and a double-faced card's front face.</summary>
    private static IEnumerable<string> NameVariants(string name)
    {
        var trimmed = name.Trim();
        yield return trimmed;
        var dashed = trimmed.Replace(", ", " - ");
        if (dashed != trimmed) yield return dashed;
        var front = trimmed.Split(" // ")[0];
        if (front != trimmed) yield return front;
    }

    /// <summary>Whether an owned card's name answers to the deck name <paramref name="deckKey"/> (normalized).</summary>
    private static bool NameMatches(string deckKey, string ownedName)
    {
        var owned = Normalize(ownedName);
        if (owned == deckKey) return true;
        if (owned == deckKey.Replace(", ", " - ")) return true;
        var deckFront = deckKey.Split(" // ")[0];
        var ownedFront = owned.Split(" // ")[0];
        return (deckFront != deckKey || ownedFront != owned) && deckFront == ownedFront;
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];

    private ICardGameService? GameService(CardGame game)
    {
        try { return cardService.GetGameService(game); }
        catch (Exception) { return null; }
    }

    /// <summary>Best-effort catalog image of the deck's printing for an Add row.</summary>
    private static string? ResolveImage(ICardGameService? gameService, DecklistEntry entry)
    {
        if (gameService is null) return null;
        try
        {
            var printings = DecklistPrintingResolver.GetPrintingsFuzzy(gameService, entry.CardName);
            if (printings.Count == 0) return null;
            var match = (entry.SetCode is null ? null
                : printings.FirstOrDefault(r => string.Equals(r.SetCode, entry.SetCode, StringComparison.OrdinalIgnoreCase)
                    && (entry.CollectorNumber is null || string.Equals(r.CollectorNumber, entry.CollectorNumber, StringComparison.OrdinalIgnoreCase))))
                ?? printings[0];
            return match.ImageUri;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string GameName(CardGame game) => game switch
    {
        CardGame.Mtg => "Magic: The Gathering",
        _ => game.ToString(),
    };
}
