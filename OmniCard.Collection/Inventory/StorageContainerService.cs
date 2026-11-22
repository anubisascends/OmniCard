using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Binder;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Sites;
using OmniCard.Shared.Storage;

namespace OmniCard.Collection.Inventory;

public sealed class StorageContainerService(IDbContextFactory<OmniCardDbContext> dbContextFactory)
    : IStorageContainerService
{
    public List<StorageContainer> GetAll()
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.StorageContainers
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToList();
    }

    public StorageContainer GetBulk()
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.StorageContainers.First(c => c.IsSystem);
    }

    // --- name uniqueness ------------------------------------------------------------------------------
    // A location's name is unique per name (trimmed, case-insensitive) + game (no game counts as its own) +
    // site. Every path that changes any of the three checks it here; the DB's unique (Name, Game, SiteId)
    // index is the backstop.

    /// <summary>The one identity a location name must be unique within.</summary>
    public static (string Name, CardGame? Game, int SiteId) NameKey(string? name, CardGame? game, int siteId) =>
        ((name ?? "").Trim().ToUpperInvariant(), game, siteId);

    public static string NameTakenMessage(string name, CardGame? game, string? siteName)
    {
        var where = string.IsNullOrWhiteSpace(siteName) ? "" : $" in {siteName}";
        return game is CardGame g
            ? $"A {g.DisplayName()} location named \"{name.Trim()}\" already exists{where}."
            : $"A location named \"{name.Trim()}\" with no fixed game already exists{where}.";
    }

    public bool NameExists(string name, CardGame? game, int? siteId, int? excludeId = null)
    {
        using var context = dbContextFactory.CreateDbContext();
        return NameExists(context, name, game, siteId ?? Site.DefaultSiteId, excludeId);
    }

    private static bool NameExists(OmniCardDbContext context, string? name, CardGame? game, int siteId, int? excludeId)
    {
        var key = NameKey(name, game, siteId);
        return context.StorageContainers
            .AsNoTracking()
            .Where(c => c.SiteId == siteId && c.Game == game && (excludeId == null || c.Id != excludeId))
            .Select(c => c.Name)
            .AsEnumerable()
            .Any(n => NameKey(n, game, siteId) == key);
    }

    private static void ThrowIfNameTaken(OmniCardDbContext context, string name, CardGame? game, int siteId, int? excludeId)
    {
        if (NameExists(context, name, game, siteId, excludeId))
            throw new LocationNameTakenException(
                NameTakenMessage(name, game, context.Sites.AsNoTracking().FirstOrDefault(s => s.Id == siteId)?.Name));
    }

    /// <summary>The edited (tracked) <paramref name="changed"/> locations whose name + game + site now clash with
    /// another location's, or with each other's (the later one by id is reported), each with the reason. For
    /// edits that change several locations at once, where a per-row check against the database would miss two
    /// selected locations landing on the same name or wrongly block two swapping games.</summary>
    public static List<(StorageContainer Container, string Error)> FindNameClashes(
        OmniCardDbContext context, IReadOnlyCollection<StorageContainer> changed)
    {
        var ids = changed.Select(c => c.Id).ToList();
        var taken = context.StorageContainers.AsNoTracking()
            .Where(c => !ids.Contains(c.Id))
            .Select(c => new { c.Name, c.Game, c.SiteId })
            .AsEnumerable()
            .Select(c => NameKey(c.Name, c.Game, c.SiteId))
            .ToHashSet();
        Dictionary<int, string>? siteNames = null;
        var clashes = new List<(StorageContainer, string)>();
        foreach (var c in changed.OrderBy(c => c.Id))
        {
            if (taken.Add(NameKey(c.Name, c.Game, c.SiteId))) continue;
            siteNames ??= context.Sites.AsNoTracking().ToDictionary(s => s.Id, s => s.Name);
            clashes.Add((c, NameTakenMessage(c.Name, c.Game, siteNames.GetValueOrDefault(c.SiteId))));
        }
        return clashes;
    }

    /// <summary><see cref="FindNameClashes"/> for one edited location: throws <see cref="LocationNameTakenException"/>.</summary>
    private static void ThrowIfNameClash(OmniCardDbContext context, StorageContainer container)
    {
        if (FindNameClashes(context, [container]) is [var clash, ..])
            throw new LocationNameTakenException(clash.Error);
    }

    public StorageContainer Create(string name, ContainerType type, int slotsPerPage = 9,
        CardGame? game = null, int? deckTypeId = null, int? siteId = null,
        IEnumerable<string>? defaultSets = null)
    {
        var trimmed = (name ?? "").Trim();
        using var context = dbContextFactory.CreateDbContext();
        ThrowIfNameTaken(context, trimmed, type == ContainerType.Bulk ? null : game, siteId ?? Site.DefaultSiteId, excludeId: null);

        var maxSort = context.StorageContainers.Any()
            ? context.StorageContainers.Max(c => c.SortOrder)
            : 0;

        var container = new StorageContainer
        {
            Name = trimmed,
            ContainerType = type,
            IsSystem = false,
            SortOrder = maxSort + 1,
            SlotsPerPage = slotsPerPage > 0 ? slotsPerPage : 9,
            SiteId = siteId ?? Site.DefaultSiteId,
        };

        // Any location but Bulk may be fixed to a game; the deck type only means something on a deck
        // box, and default sets only alongside a game (they're that game's codes).
        if (type != ContainerType.Bulk)
        {
            container.Game = game;
            container.DefaultSets = game is null ? null : StorageContainer.JoinSetCodes(defaultSets);
        }
        if (type == ContainerType.DeckBox)
            container.DeckTypeId = deckTypeId;

        // New binders start with one double-sided sheet (front + back), the default the user asked
        // for; non-binder containers ignore this and keep the single-page default.
        if (type == ContainerType.Binder)
        {
            var layout = BinderSheetLayout.NewDefault();
            container.SheetSides = layout.Serialize();
            container.TotalPages = layout.TotalPages;
        }

        context.StorageContainers.Add(container);
        context.SaveChanges();
        return container;
    }

    public void SetDeckBox(int containerId, CardGame game, int? deckTypeId)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        if (container.ContainerType != ContainerType.DeckBox)
            throw new InvalidOperationException("Only deck boxes can be assigned a game system.");

        // Guard the single-game invariant: reject if the box already holds cards from a different game.
        var conflicting = context.Lots
            .Where(l => l.LocationId == containerId
                && l.Product.Category == ProductCategory.Single
                && l.Product.Game != game)
            .Select(l => l.Product.Game)
            .Distinct()
            .ToList();
        if (conflicting.Count > 0)
            throw new InvalidOperationException(
                $"This deck box already contains cards from another game ({string.Join(", ", conflicting)}). " +
                "Move those cards out before assigning a different game.");

        // Default sets are the old game's codes; a new game starts with none.
        if (container.Game != game)
            container.DefaultSets = null;
        container.Game = game;
        container.DeckTypeId = deckTypeId;
        ThrowIfNameClash(context, container);
        context.SaveChanges();
    }

    public void SetCardFilter(int containerId, CardGame? game, IEnumerable<string>? defaultSets)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        if (ApplyCardFilter(context, container, game, defaultSets) is { } error)
            throw new InvalidOperationException(error);
        ThrowIfNameClash(context, container);
        context.SaveChanges();
    }

    /// <summary>The one rule for giving a tracked <paramref name="container"/> a game (null = any game) and
    /// default sets, shared by the single and bulk edits. Returns why it can't (and changes nothing), or null
    /// once applied — the caller saves, after checking the new game doesn't clash with another location's name
    /// (<see cref="FindNameClashes"/>).</summary>
    public static string? ApplyCardFilter(OmniCardDbContext context, StorageContainer container,
        CardGame? game, IEnumerable<string>? defaultSets)
    {
        if (container.IsSystem || container.ContainerType == ContainerType.Bulk)
            return "Bulk holds every game, so it can't be given a game or sets.";
        if (container.ContainerType == ContainerType.DeckBox && game is null)
            return "A deck box must be assigned a game.";

        var sets = game is null ? null : StorageContainer.JoinSetCodes(defaultSets);
        if (sets is { Length: > StorageContainer.MaxDefaultSetsLength })
            return "Too many default sets.";

        if (game is CardGame fixedGame && container.Game != fixedGame)
        {
            // The game is a hard rule, so it can't be set over cards that already break it.
            var containerId = container.Id;
            var conflicting = context.Lots
                .Where(l => l.LocationId == containerId
                    && l.Product.Category == ProductCategory.Single
                    && l.Product.Game != fixedGame)
                .Select(l => l.Product.Game)
                .Distinct()
                .ToList();
            if (conflicting.Count > 0)
                return $"This location already holds cards from another game ({string.Join(", ", conflicting.Select(g => g.DisplayName()))}). " +
                    "Move those cards out before fixing it to one game.";

            // A deck type belongs to one game, so it can't survive a game change.
            container.DeckTypeId = null;
        }

        container.Game = game;
        container.DefaultSets = sets;
        return null;
    }

    public List<DeckBoxNeedsGame> GetDeckBoxesMissingGame()
    {
        using var context = dbContextFactory.CreateDbContext();
        var deckBoxes = context.StorageContainers.AsNoTracking()
            .Where(c => c.ContainerType == ContainerType.DeckBox && c.Game == null)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToList();

        var result = new List<DeckBoxNeedsGame>(deckBoxes.Count);
        foreach (var box in deckBoxes)
        {
            var games = context.Lots.AsNoTracking()
                .Where(l => l.LocationId == box.Id && l.Product.Category == ProductCategory.Single)
                .Select(l => l.Product.Game)
                .Distinct()
                .ToList();
            result.Add(new DeckBoxNeedsGame
            {
                Id = box.Id,
                Name = box.Name,
                InferredGame = games.Count == 1 ? games[0] : null,
                CardGames = games,
            });
        }
        return result;
    }

    public void Rename(int id, string newName)
    {
        var trimmed = (newName ?? "").Trim();
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(id)
            ?? throw new InvalidOperationException($"Container {id} not found");
        if (container.IsSystem)
            throw new InvalidOperationException("Cannot rename system container");
        ThrowIfNameTaken(context, trimmed, container.Game, container.SiteId, excludeId: id);

        container.Name = trimmed;
        context.SaveChanges();
    }

    public void SetSite(int containerId, int siteId)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        if (container.IsSystem)
            throw new InvalidOperationException("The Bulk location always stays in the default site.");
        if (!context.Sites.Any(s => s.Id == siteId))
            throw new InvalidOperationException($"Site {siteId} not found");

        container.SiteId = siteId;
        ThrowIfNameClash(context, container);
        context.SaveChanges();
    }

    public void Delete(int id, bool moveCardsToBulk = true)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(id)
            ?? throw new InvalidOperationException($"Container {id} not found");
        if (container.IsSystem)
            throw new InvalidOperationException("Cannot delete system container");

        RemoveContainer(context, container, moveCardsToBulk);
        context.SaveChanges();
    }

    /// <summary>The one rule for deleting a tracked, non-system <paramref name="container"/>, shared by the
    /// single and bulk deletes: its singles move to Bulk or are deleted with their eBay listings and flag
    /// history; sealed lots just lose their location (FK SetNull). Bulk is only looked up when there are cards
    /// to move (throws when it doesn't exist). The caller saves.</summary>
    public static void RemoveContainer(OmniCardDbContext context, StorageContainer container, bool moveCardsToBulk)
    {
        if (container.IsSystem)
            throw new InvalidOperationException("Cannot delete system container");
        var id = container.Id;
        var lots = context.Lots.Include(l => l.Product)
            .Where(l => l.LocationId == id && l.Product.Category == ProductCategory.Single)
            .ToList();

        if (moveCardsToBulk && lots.Count > 0)
        {
            var bulkId = context.StorageContainers.Where(c => c.IsSystem).Select(c => (int?)c.Id).FirstOrDefault()
                ?? throw new InvalidOperationException("There's no Bulk location to move the cards to.");
            foreach (var lot in lots)
            {
                DeckOrigin.BeforeMove(context, lot, bulkId);
                lot.LocationId = bulkId;
                lot.Page = null;
                lot.Slot = null;
                lot.Section = null;
            }
        }
        else if (!moveCardsToBulk)
        {
            var lotIds = lots.Select(l => l.Id).ToList();
            context.EbayListings.RemoveRange(context.EbayListings.Where(e => lotIds.Contains(e.LotId)));
            context.FlagResolutions.RemoveRange(context.FlagResolutions.Where(f => lotIds.Contains(f.LotId)));
            context.Lots.RemoveRange(lots);
        }

        context.StorageContainers.Remove(container);
    }

    public int GetCardCount(int containerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.Lots.Count(l => l.LocationId == containerId && l.Product.Category == ProductCategory.Single);
    }

    public void SetCoverCard(int containerId, int? cardId)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        container.CoverCardId = cardId;
        context.SaveChanges();
    }

    public List<CollectionCard> GetCardsInContainer(int containerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.Lots.AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.LocationId == containerId && l.Product.Category == ProductCategory.Single)
            .ToList()
            .Select(l => CollectionCardMapper.ToDto(l, l.Product, 0m))
            .OrderBy(c => c.Name)
            .ToList();
    }

    public void SetExcludeFromDeckCheck(int containerId, bool exclude)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        container.ExcludeFromDeckCheck = exclude;
        context.SaveChanges();
    }

    public void SetAlwaysAvailable(int containerId, bool alwaysAvailable)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        // The system Bulk location is always available intrinsically; leave its stored flag alone.
        if (container.IsSystem)
            return;
        container.AlwaysAvailable = alwaysAvailable;
        context.SaveChanges();
    }

    public BinderLayout GetBinderLayout(int containerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        var sheets = BinderSheetLayout.Parse(container.SheetSides, container.TotalPages);
        return new BinderLayout
        {
            SlotsPerPage = container.SlotsPerPage,
            TotalPages = sheets.TotalPages,
            Columns = container.Columns,
            SheetSides = sheets.Sides,
        };
    }

    public void AddBinderSheet(int containerId, bool doubleSided)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        // Appending a sheet never moves an existing page, so no lot remapping is needed.
        var layout = BinderSheetLayout.Parse(container.SheetSides, container.TotalPages).Append(doubleSided);
        container.SheetSides = layout.Serialize();
        container.TotalPages = layout.TotalPages;
        context.SaveChanges();
    }

    public BinderSheetInfo GetSheetForPage(int containerId, int page)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        var layout = BinderSheetLayout.Parse(container.SheetSides, container.TotalPages);
        var sheetIndex = layout.SheetIndexOfPage(page);
        if (sheetIndex < 0)
            throw new InvalidOperationException($"Page {page} is not in binder {containerId}.");

        var firstPage = layout.FirstPageOfSheet(sheetIndex);
        var sides = layout.SidesOfSheet(sheetIndex);
        var lastPageExclusive = firstPage + sides;
        var cardCount = context.Lots.Count(l => l.LocationId == containerId
            && l.Page >= firstPage && l.Page < lastPageExclusive
            && l.Product.Category == ProductCategory.Single);

        return new BinderSheetInfo
        {
            SheetIndex = sheetIndex,
            FirstPage = firstPage,
            Sides = sides,
            TotalSheets = layout.SheetCount,
            CardCount = cardCount,
            Pages = Enumerable.Range(firstPage, sides).ToList(),
        };
    }

    public List<BinderSheetInfo> GetSheets(int containerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        var layout = BinderSheetLayout.Parse(container.SheetSides, container.TotalPages);

        var placedPages = context.Lots.AsNoTracking()
            .Where(l => l.LocationId == containerId && l.Page != null && l.Product.Category == ProductCategory.Single)
            .Select(l => l.Page)
            .ToList();
        var counts = placedPages.Where(p => p.HasValue)
            .GroupBy(p => p!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var sheets = new List<BinderSheetInfo>();
        for (var i = 0; i < layout.SheetCount; i++)
        {
            var first = layout.FirstPageOfSheet(i);
            var sides = layout.SidesOfSheet(i);
            var pages = Enumerable.Range(first, sides).ToList();
            sheets.Add(new BinderSheetInfo
            {
                SheetIndex = i,
                FirstPage = first,
                Sides = sides,
                TotalSheets = layout.SheetCount,
                CardCount = pages.Sum(p => counts.GetValueOrDefault(p)),
                Pages = pages,
            });
        }

        return sheets;
    }

    public void InsertBinderSheet(int containerId, int insertIndex, bool doubleSided)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        var layout = BinderSheetLayout.Parse(container.SheetSides, container.TotalPages);
        if (insertIndex < 0 || insertIndex > layout.SheetCount)
            throw new ArgumentOutOfRangeException(nameof(insertIndex));

        var (newLayout, remap) = layout.InsertSheet(insertIndex, doubleSided);
        ApplyPageRemap(context, containerId, newLayout, remap);
    }

    public void MoveBinderSheet(int containerId, int fromPage, int toIndex)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        var layout = BinderSheetLayout.Parse(container.SheetSides, container.TotalPages);
        var fromIndex = layout.SheetIndexOfPage(fromPage);
        if (fromIndex < 0)
            throw new InvalidOperationException($"Page {fromPage} is not in binder {containerId}.");
        if (toIndex < 0 || toIndex >= layout.SheetCount)
            throw new ArgumentOutOfRangeException(nameof(toIndex));

        var (newLayout, remap) = layout.MoveSheet(fromIndex, toIndex);
        ApplyPageRemap(context, containerId, newLayout, remap);
    }

    public void RemoveBinderSheet(int containerId, int page)
    {
        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        var layout = BinderSheetLayout.Parse(container.SheetSides, container.TotalPages);
        var sheetIndex = layout.SheetIndexOfPage(page);
        if (sheetIndex < 0)
            throw new InvalidOperationException($"Page {page} is not in binder {containerId}.");
        if (layout.SheetCount <= 1)
            throw new InvalidOperationException("A binder must keep at least one page.");

        var (newLayout, remap) = layout.RemoveSheet(sheetIndex);
        ApplyPageRemap(context, containerId, newLayout, remap);
    }

    /// <summary>Applies a page-number remap (from a <see cref="BinderSheetLayout"/> transform) to
    /// every placed lot in the binder in one pass, then persists the new sheet layout. A null
    /// target unplaces the lot (Page/Slot cleared, staying in the binder); any other target renames
    /// the page, keeping the slot. Shared by remove/insert/move.</summary>
    private static void ApplyPageRemap(
        OmniCardDbContext context, int containerId, BinderSheetLayout newLayout,
        IReadOnlyDictionary<int, int?> pageRemap)
    {
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        if (pageRemap.Count > 0)
        {
            var placed = context.Lots.Where(l => l.LocationId == containerId && l.Page != null).ToList();
            foreach (var lot in placed)
            {
                if (lot.Page is int p && pageRemap.TryGetValue(p, out var newPage))
                {
                    if (newPage is null) { lot.Page = null; lot.Slot = null; }
                    else lot.Page = newPage.Value;
                }
            }
        }

        // Dividers follow their page the same way (a removed page hands its divider to the next page).
        BinderDividerService.ApplyRemap(context, containerId, pageRemap, newLayout.TotalPages);

        container.SheetSides = newLayout.Serialize();
        container.TotalPages = newLayout.TotalPages;
        context.SaveChanges();
    }

    public void ShiftPage(int containerId, int page, int deltaPages, BinderShiftScope scope)
    {
        if (deltaPages == 0) return;

        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        var totalPages = BinderSheetLayout.Parse(container.SheetSides, container.TotalPages).TotalPages;

        var placed = context.Lots
            .Where(l => l.LocationId == containerId && l.Page != null
                && l.Product.Category == ProductCategory.Single)
            .ToList();
        if (placed.Count == 0) return;

        // The affected pages form a contiguous block: just this page, this page and everything before
        // it, or this page and everything after it. Moving cards keep their slot and shift by the same
        // delta, so they can only ever collide with cards that stay put — never with each other.
        bool IsMoving(int p) => scope switch
        {
            BinderShiftScope.OnlyThisPage => p == page,
            BinderShiftScope.ThisAndBefore => p <= page,
            BinderShiftScope.ThisAndAfter => p >= page,
            _ => false,
        };

        var moving = placed.Where(l => IsMoving(l.Page!.Value)).ToList();
        if (moving.Count == 0) return;

        // Slots occupied by cards that are NOT moving — a moving card landing on one of these is a
        // collision.
        var stayingOccupied = placed
            .Where(l => !IsMoving(l.Page!.Value))
            .Select(l => (l.Page!.Value, l.Slot))
            .ToHashSet();

        var amount = Math.Abs(deltaPages);
        var pagesWord = amount == 1 ? "page" : "pages";

        foreach (var lot in moving)
        {
            var newPage = lot.Page!.Value + deltaPages;
            if (newPage < 1 || newPage > totalPages)
                throw new InvalidOperationException(
                    $"Shifting by {amount} {pagesWord} would move a card off the binder " +
                    $"(it has {totalPages} page{(totalPages == 1 ? "" : "s")}). " +
                    "Add more pages first, choose a different direction or scope, or shift fewer pages.");

            if (stayingOccupied.Contains((newPage, lot.Slot)))
                throw new InvalidOperationException(
                    $"Shifting would move a card onto page {newPage}, which already has a card in that " +
                    "slot. Clear the destination first, or widen the scope so those pages move too.");
        }

        foreach (var lot in moving)
            lot.Page = lot.Page!.Value + deltaPages;

        // A divider on a shifted page goes with its cards, so its section still starts where they are.
        BinderDividerService.ApplyShift(context, containerId, IsMoving, deltaPages, totalPages);

        context.SaveChanges();
    }

    public void SetSlotsPerPage(int containerId, int slotsPerPage)
    {
        if (slotsPerPage <= 0)
            throw new ArgumentOutOfRangeException(nameof(slotsPerPage), "Slots per page must be positive.");

        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        container.SlotsPerPage = slotsPerPage;
        context.SaveChanges();
    }

    public void SetColumns(int containerId, int columns)
    {
        if (columns <= 0)
            throw new ArgumentOutOfRangeException(nameof(columns), "Columns must be positive.");

        using var context = dbContextFactory.CreateDbContext();
        var container = context.StorageContainers.Find(containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");
        container.Columns = columns;
        context.SaveChanges();
    }

    public List<CollectionCard> GetPlacedCardsOnPage(int containerId, int page)
    {
        using var context = dbContextFactory.CreateDbContext();
        var cards = context.Lots.AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.LocationId == containerId && l.Page == page && l.Product.Category == ProductCategory.Single)
            .ToList()
            .Select(l => CollectionCardMapper.ToDto(l, l.Product, 0m))
            .ToList();

        // Attach eBay listing state so binder-slot tiles/context-menu actions (List/View/End on
        // eBay) see accurate status, same as the main collection list.
        var lotIds = cards.Select(c => c.Id).ToList();
        var listingsByLotId = context.EbayListings.AsNoTracking()
            .Where(l => lotIds.Contains(l.LotId))
            .ToList()
            .ToDictionary(l => l.LotId);
        foreach (var card in cards)
            card.EbayListing = listingsByLotId.GetValueOrDefault(card.Id);

        return cards;
    }

    public void AssignCardToSlot(int lotId, int containerId, int page, int slot)
    {
        using var context = dbContextFactory.CreateDbContext();

        var draggedLot = context.Lots.Find(lotId)
            ?? throw new InvalidOperationException($"Lot {lotId} not found");
        if (draggedLot.LocationId != containerId)
            throw new InvalidOperationException($"Lot {lotId} is not in container {containerId}");

        var occupant = context.Lots.FirstOrDefault(l =>
            l.LocationId == containerId && l.Page == page && l.Slot == slot && l.Id != lotId);

        if (occupant is not null)
        {
            if (draggedLot.Page is null && draggedLot.Slot is null)
            {
                // Dragged from the unplaced pool: displaced occupant returns to the pool.
                occupant.Page = null;
                occupant.Slot = null;
            }
            else
            {
                // Dragged from another slot: swap coordinates.
                occupant.Page = draggedLot.Page;
                occupant.Slot = draggedLot.Slot;
            }
        }

        draggedLot.LocationId = containerId;
        draggedLot.Page = page;
        draggedLot.Slot = slot;

        context.SaveChanges();
    }

    public void UnassignFromPage(int lotId)
    {
        using var context = dbContextFactory.CreateDbContext();
        var lot = context.Lots.Find(lotId)
            ?? throw new InvalidOperationException($"Lot {lotId} not found");
        lot.Page = null;
        lot.Slot = null;
        context.SaveChanges();
    }
}
