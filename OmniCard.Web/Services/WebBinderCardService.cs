using Microsoft.EntityFrameworkCore;
using OmniCard.CardMatching;
using OmniCard.Collection;
using OmniCard.Collection.Inventory;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Settings;

namespace OmniCard.Web.Services;

/// <summary>
/// The subset of card-mutation logic the web binder editor needs, reimplemented against a writable
/// <see cref="OmniCardDbContext"/>. The desktop <c>CardService</c> can't be reused here: it pulls in
/// WPF / System.Drawing / scanner / OCR dependencies and runs <c>EnsureCreated</c> in its
/// constructor. These methods mirror the equivalents in <c>OmniCard.Collection/CardService.cs</c> —
/// keep them in sync. Read filtering is shared via <see cref="CollectionQueryBuilder"/>; the
/// find-or-create-Product + identity-copy helpers are duplicated below (they are small, stable, and
/// self-contained) rather than touching CardService's many desktop call sites.
/// </summary>
public sealed class WebBinderCardService
{
    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;
    private readonly IDataPathService _dataPathService;
    private readonly IReadOnlyDictionary<CardGame, ICardGameService>? _gameServices;

    // Takes the factory interface (not the concrete writable type) so it's unit-testable with an
    // in-memory factory. In production it's constructed explicitly in Program.cs with the writable
    // factory — never via container constructor injection — so it can't accidentally bind to the
    // app's read-only IDbContextFactory<OmniCardDbContext>. The game services (optional) let the
    // Scryfall-syntax filter resolve game-specific fields (element:, might:, …) in binder presets.
    public WebBinderCardService(
        IDbContextFactory<OmniCardDbContext> dbFactory,
        IDataPathService dataPathService,
        IReadOnlyDictionary<CardGame, ICardGameService>? gameServices = null)
    {
        _dbFactory = dbFactory;
        _dataPathService = dataPathService;
        _gameServices = gameServices;
    }

    /// <summary>Cards in the binder that have no page assignment (the "Unplaced pool"), narrowed by
    /// the optional Scryfall-syntax filter. Mirrors <c>CardService.GetUnplacedBinderCards</c>.</summary>
    public List<CollectionCard> GetUnplacedBinderCards(int containerId, FilterPreset? filterPreset)
    {
        using var context = _dbFactory.CreateDbContext();
        return CollectionQueryBuilder.BuildFilteredQuery(context, "", null, containerId, filterPreset, _gameServices)
            .Where(c => c.Page == null)
            .OrderBy(c => c.Name)
            .ToList();
    }

    public List<CollectionCard> GetCollectionCards(IEnumerable<int> cardIds)
    {
        using var context = _dbFactory.CreateDbContext();
        var ids = cardIds.ToList();
        return context.Lots.AsNoTracking()
            .Include(l => l.Product)
            .Where(l => ids.Contains(l.Id) && l.Product.Category == ProductCategory.Single)
            .ToList()
            .Select(l => CollectionCardMapper.ToDto(l, l.Product, 0m))
            .ToList();
    }

    /// <summary>Whether the collection holds no lot of this card yet (by game + card id, regardless of
    /// finish) — i.e. scanning it would add a card you don't already own. Drives the scan page's
    /// "new card" gold-star badge. Each call opens its own context, so it's safe to call concurrently
    /// during a batch match (unlike the game services' shared read context).</summary>
    public bool IsNewCard(CardGame game, string gameCardId)
    {
        if (string.IsNullOrEmpty(gameCardId)) return false;
        using var context = _dbFactory.CreateDbContext();
        return !context.Lots.AsNoTracking()
            .Any(l => l.Product.Game == game && l.Product.GameCardId == gameCardId);
    }

    /// <summary>Of the given catalog ids, the subset the collection already owns at least one lot of
    /// (by game + card id, regardless of finish/condition). Bulk companion to <see cref="IsNewCard"/> —
    /// one query for a whole list rather than a probe per card.</summary>
    public HashSet<string> GetOwnedGameCardIds(CardGame game, IEnumerable<string> gameCardIds)
    {
        var ids = gameCardIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (ids.Count == 0)
            return [];
        using var context = _dbFactory.CreateDbContext();
        return context.Lots.AsNoTracking()
            .Where(l => l.Product.Game == game && l.Product.GameCardId != null && ids.Contains(l.Product.GameCardId))
            .Select(l => l.Product.GameCardId!)
            .Distinct()
            .ToHashSet();
    }

    public void MoveCardsToContainer(IEnumerable<int> cardIds, int containerId, string? section = null)
    {
        using var context = _dbFactory.CreateDbContext();
        var ids = cardIds.ToList();
        var lots = context.Lots.Include(l => l.Product)
            .Where(l => ids.Contains(l.Id) && l.Product.Category == ProductCategory.Single).ToList();

        // Hard block: a game-locked deck box rejects cards from other games (see DeckBoxGameGuard).
        DeckBoxGameGuard.ValidateIncoming(context, containerId, lots.Select(l => l.Product.Game).Distinct());

        foreach (var lot in lots)
        {
            lot.LocationId = containerId;
            lot.Page = null;
            lot.Slot = null;
            lot.Section = section;

            context.Movements.Add(new InventoryMovement
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                Type = MovementType.Move,
                Quantity = 1,
                Note = section,
            });
        }
        context.SaveChanges();
    }

    public void BulkUpdateField(IEnumerable<int> cardIds, Action<CollectionCard> update)
    {
        using var context = _dbFactory.CreateDbContext();
        var ids = cardIds.ToList();
        var lots = context.Lots.Include(l => l.Product)
            .Where(l => ids.Contains(l.Id) && l.Product.Category == ProductCategory.Single)
            .ToList();
        var productCache = new Dictionary<(CardGame Game, string GameCardId, bool Foil, string? FoilType), Product>();

        foreach (var lot in lots)
        {
            var dto = CollectionCardMapper.ToDto(lot, lot.Product, 0m);
            update(dto);
            ApplyIdentityAndCopyAttrs(context, lot, dto, productCache);
        }

        context.SaveChanges();
    }

    public void SetCondition(IEnumerable<int> cardIds, string condition)
        => BulkUpdateField(cardIds, c => c.Condition = condition);

    public void SetFoil(IEnumerable<int> cardIds, bool isFoil)
        => BulkUpdateField(cardIds, c => c.IsFoil = isFoil);

    public void UpdateCollectionCard(CollectionCard card)
    {
        using var context = _dbFactory.CreateDbContext();
        var lot = context.Lots.Include(l => l.Product)
            .FirstOrDefault(l => l.Id == card.Id && l.Product.Category == ProductCategory.Single);
        if (lot is null)
            return;

        ApplyIdentityAndCopyAttrs(context, lot, card);
        context.SaveChanges();
    }

    /// <summary>Imports parsed collection cards as new lots (find-or-create product per card).
    /// Ports <c>CardService.ImportCollectionCards</c> for the web write path. <paramref name="skipDuplicates"/>
    /// skips a card when a lot with the same product identity + condition already exists.</summary>
    public int ImportCollectionCards(IEnumerable<CollectionCard> cards, bool skipDuplicates)
    {
        using var context = _dbFactory.CreateDbContext();
        var cardList = cards as ICollection<CollectionCard> ?? cards.ToList();

        // Hard block: reject an import that would place a card into a game-locked deck box of another
        // game. Imported cards can target different locations, so validate each target's games.
        foreach (var group in cardList.Where(c => c.ContainerId != null).GroupBy(c => c.ContainerId))
            DeckBoxGameGuard.ValidateIncoming(context, group.Key, group.Select(c => c.Game).Distinct());

        var productCache = new Dictionary<(CardGame Game, string GameCardId, bool Foil, string? FoilType), Product>();
        var imported = 0;

        foreach (var card in cardList)
        {
            var cardFoilType = card.IsFoil ? card.FoilType : null;
            if (skipDuplicates)
            {
                var exists = context.Lots.Any(l => l.Product.Game == card.Game
                    && l.Product.GameCardId == card.GameCardId
                    && l.Product.Foil == card.IsFoil
                    && l.Product.FoilType == cardFoilType
                    && l.Condition == card.Condition);
                if (exists)
                    continue;
            }

            var product = FindOrCreateProduct(context, productCache, card.Game, card.GameCardId, card.IsFoil,
                cardFoilType, card.Name, card.SetCode, card.SetName, card.Number, card.Rarity, card.ImageUri,
                card.Color, card.CardType);

            context.Lots.Add(new InventoryLot
            {
                Product = product,
                Condition = card.Condition,
                Note = card.Note,
                Quantity = Math.Max(1, card.Quantity),
                UnitCost = card.PurchasePrice,
                AcquisitionDate = card.DateAdded,
                LocationId = card.ContainerId,
                Page = card.Page,
                Slot = card.Slot,
                Section = card.Section,
            });
            imported++;
        }

        context.SaveChanges();
        return imported;
    }

    /// <summary>Imports confirmed scans as new lots and returns the created lot id per input card
    /// (parallel to <paramref name="cards"/>). Unlike <see cref="ImportCollectionCards"/> this never
    /// skips duplicates (a scanned card is a real physical copy) and surfaces the ids so the caller
    /// can attach per-copy tags after the lots exist.</summary>
    public IReadOnlyList<int> AddScannedLots(IReadOnlyList<CollectionCard> cards)
    {
        using var context = _dbFactory.CreateDbContext();

        // Hard block: scanning into a game-locked deck box rejects cards from other games. All scanned
        // cards target the same location, so validate the batch's games up front.
        DeckBoxGameGuard.ValidateIncoming(context,
            cards.Select(c => c.ContainerId).FirstOrDefault(id => id != null),
            cards.Select(c => c.Game).Distinct());

        var productCache = new Dictionary<(CardGame Game, string GameCardId, bool Foil, string? FoilType), Product>();
        var lots = new List<InventoryLot>(cards.Count);

        foreach (var card in cards)
        {
            var lot = BuildLot(context, productCache, card);
            context.Lots.Add(lot);
            lots.Add(lot);
        }

        context.SaveChanges();
        return lots.Select(l => l.Id).ToList();
    }

    /// <summary>Constructs a new (untracked) <see cref="InventoryLot"/> for a scanned card, resolving
    /// or creating its <see cref="Product"/>. Shared by the scan-commit and audit-commit paths.</summary>
    private static InventoryLot BuildLot(
        OmniCardDbContext context,
        Dictionary<(CardGame Game, string GameCardId, bool Foil, string? FoilType), Product> productCache,
        CollectionCard card)
    {
        var product = FindOrCreateProduct(context, productCache, card.Game, card.GameCardId, card.IsFoil,
            card.IsFoil ? card.FoilType : null, card.Name, card.SetCode, card.SetName, card.Number, card.Rarity,
            card.ImageUri, card.Color, card.CardType);

        return new InventoryLot
        {
            Product = product,
            Condition = card.Condition,
            Note = card.Note,
            Quantity = Math.Max(1, card.Quantity),
            UnitCost = card.PurchasePrice,
            AcquisitionDate = card.DateAdded,
            LocationId = card.ContainerId,
        };
    }

    /// <summary>Identity key for audit matching: a card's game id (foil-agnostic — foil and non-foil
    /// printings of the same card share a <c>GameCardId</c> and should reconcile together), falling
    /// back to set code + collector number when no game id is present. Shared with the controller so
    /// scanned items and stored lots key identically.</summary>
    public static string AuditIdentityKey(string? gameCardId, string? setCode, string? collectorNumber)
    {
        var id = gameCardId?.Trim();
        if (!string.IsNullOrEmpty(id)) return "id:" + id.ToLowerInvariant();
        return "sc:" + (setCode ?? "").Trim().ToLowerInvariant() + "|" + (collectorNumber ?? "").Trim().ToLowerInvariant();
    }

    /// <summary>One card line in an audit outcome bucket.</summary>
    public sealed record AuditLine(string Name, string SetCode, string CollectorNumber, string? Condition, bool IsFoil, int Quantity);

    /// <summary>Result of reconciling a location against a confirmed scan. <see cref="AddedLots"/>
    /// pairs each newly-created lot's id with its identity key so the caller can attach tags.</summary>
    public sealed record AuditReconcileResult(
        IReadOnlyList<AuditLine> Matched,
        IReadOnlyList<AuditLine> NotFound,
        IReadOnlyList<AuditLine> Added,
        int UpdatedCount,
        IReadOnlyList<(string IdentityKey, int LotId)> AddedLots);

    /// <summary>Makes the confirmed scan the source of truth for a location: cards present in both are
    /// <b>matched</b> (their stored lots are kept, but condition/foil is overwritten from the scan);
    /// cards scanned but not previously present are <b>added</b> as new lots; cards expected at the
    /// location but not scanned are <b>not found</b> and their lots are deleted. Quantities are
    /// reconciled per identity. Only single-card lots participate (sealed product is untouched).</summary>
    public AuditReconcileResult ReconcileLocationAudit(int containerId, IReadOnlyList<CollectionCard> scanned)
    {
        using var context = _dbFactory.CreateDbContext();

        // Same hard block as scanning: a game-locked deck box rejects cards from other games.
        DeckBoxGameGuard.ValidateIncoming(context, containerId, scanned.Select(c => c.Game).Distinct());

        var expectedLots = context.Lots
            .Include(l => l.Product)
            .Where(l => l.LocationId == containerId && l.Product.Category == ProductCategory.Single)
            .ToList();

        var expectedByKey = new Dictionary<string, List<InventoryLot>>();
        foreach (var lot in expectedLots)
        {
            var key = AuditIdentityKey(lot.Product.GameCardId, lot.Product.SetCode, lot.Product.CollectorNumber);
            if (!expectedByKey.TryGetValue(key, out var list)) expectedByKey[key] = list = new List<InventoryLot>();
            list.Add(lot);
        }

        // Keep the individual scanned items per identity (not just a total): added copies are
        // materialized as separate lots — one per scanned item, preserving its own condition/foil/
        // price/note — exactly like the normal scan commit (AddScannedLots). Collapsing them into a
        // single quantity>1 lot would undercount the location, since a lot counts as one card
        // regardless of quantity (StorageContainerService.GetCardCount).
        var scannedByKey = new Dictionary<string, List<CollectionCard>>();
        foreach (var card in scanned)
        {
            var key = AuditIdentityKey(card.GameCardId, card.SetCode, card.Number);
            if (!scannedByKey.TryGetValue(key, out var list)) scannedByKey[key] = list = new List<CollectionCard>();
            list.Add(card);
        }

        var cache = new Dictionary<(CardGame Game, string GameCardId, bool Foil, string? FoilType), Product>();
        var matched = new List<AuditLine>();
        var added = new List<AuditLine>();
        var notFound = new List<AuditLine>();
        var addedLots = new List<(string Key, InventoryLot Lot)>();
        var updated = 0;

        foreach (var (key, items) in scannedByKey)
        {
            expectedByKey.TryGetValue(key, out var expLots);
            expLots ??= new List<InventoryLot>();

            var rep = items[0];
            var expectedCount = expLots.Sum(l => Math.Max(1, l.Quantity));
            var scannedCount = items.Sum(c => Math.Max(1, c.Quantity));
            var matchedCount = Math.Min(expectedCount, scannedCount);

            // Delete/trim the expected copies beyond what was scanned ("not found").
            var toRemove = expectedCount - matchedCount;
            foreach (var lot in expLots.OrderBy(l => Math.Max(1, l.Quantity)))
            {
                if (toRemove <= 0) break;
                var q = Math.Max(1, lot.Quantity);
                if (q <= toRemove) { context.Lots.Remove(lot); lot.Quantity = 0; toRemove -= q; }
                else { lot.Quantity = q - toRemove; toRemove = 0; }
            }

            // Overwrite condition/foil on the surviving (matched) lots from the scan.
            foreach (var lot in expLots.Where(l => l.Quantity > 0))
                if (OverwriteFromScan(context, cache, lot, rep)) updated++;

            if (matchedCount > 0) matched.Add(Line(rep, matchedCount));
            var notFoundCount = expectedCount - matchedCount;
            if (notFoundCount > 0) notFound.Add(LineFromLot(expLots[0], notFoundCount));

            // The first `expectedCount` scanned copies are considered matched to the kept lots; the
            // remainder are genuinely new. Walk the scanned items copy-by-copy, adding one lot per
            // item (splitting an item only if the matched/added boundary falls inside it).
            var surplus = scannedCount - expectedCount;
            if (surplus > 0)
            {
                var skip = expectedCount; // copies already represented by the kept expected lots
                var addedForKey = 0;
                foreach (var item in items)
                {
                    var q = Math.Max(1, item.Quantity);
                    if (skip >= q) { skip -= q; continue; } // this whole item matched an existing copy
                    var addQ = q - skip;
                    skip = 0;
                    var lot = BuildLot(context, cache, item);
                    lot.Quantity = addQ;
                    context.Lots.Add(lot);
                    addedLots.Add((key, lot));
                    addedForKey += addQ;
                }
                if (addedForKey > 0) added.Add(Line(rep, addedForKey));
            }
        }

        // Expected identities the scan never saw → delete every copy ("not found").
        foreach (var (key, expLots) in expectedByKey)
        {
            if (scannedByKey.ContainsKey(key)) continue;
            var cnt = expLots.Sum(l => Math.Max(1, l.Quantity));
            foreach (var lot in expLots) context.Lots.Remove(lot);
            notFound.Add(LineFromLot(expLots[0], cnt));
        }

        context.SaveChanges();

        return new AuditReconcileResult(matched, notFound, added, updated,
            addedLots.Select(a => (a.Key, a.Lot.Id)).ToList());

        static AuditLine Line(CollectionCard c, int qty) =>
            new(c.Name, c.SetCode ?? "", c.Number ?? "", c.Condition, c.IsFoil, qty);
        static AuditLine LineFromLot(InventoryLot l, int qty) =>
            new(l.Product.Name, l.Product.SetCode ?? "", l.Product.CollectorNumber ?? "", l.Condition, l.Product.Foil, qty);
    }

    /// <summary>Overwrites a matched lot's condition and foil from the scanned copy (the audit is the
    /// source of truth for those fields). Reassigns the lot to the correct foil-variant product when
    /// the foil state changed, preserving the product's identity fields. Returns whether anything
    /// changed. Purchase price, notes, tags and binder position on matched lots are left intact.</summary>
    private static bool OverwriteFromScan(
        OmniCardDbContext context,
        Dictionary<(CardGame Game, string GameCardId, bool Foil, string? FoilType), Product> cache,
        InventoryLot lot, CollectionCard scanned)
    {
        var changed = false;
        if ((lot.Condition ?? "") != (scanned.Condition ?? "")) { lot.Condition = scanned.Condition; changed = true; }

        var foilType = scanned.IsFoil ? scanned.FoilType : null;
        var product = lot.Product;
        if (product.Foil != scanned.IsFoil || product.FoilType != foilType)
        {
            lot.Product = FindOrCreateProduct(context, cache, product.Game, product.GameCardId ?? "", scanned.IsFoil, foilType,
                product.Name, product.SetCode, product.SetName, product.CollectorNumber, product.Rarity,
                product.ImageUri, product.Color, product.CardType);
            changed = true;
        }
        return changed;
    }

    /// <summary>Sets the owned copy count on a lot. Kept separate from
    /// <see cref="UpdateCollectionCard"/> (which mirrors the binder editor's identity/attribute copy
    /// and does not touch quantity, since binder pockets hold single copies).</summary>
    public void SetQuantity(int lotId, int quantity)
    {
        using var context = _dbFactory.CreateDbContext();
        var lot = context.Lots.FirstOrDefault(l => l.Id == lotId && l.Product.Category == ProductCategory.Single);
        if (lot is null)
            return;
        lot.Quantity = Math.Max(1, quantity);
        context.SaveChanges();
    }

    /// <summary>Bulk form of <see cref="SetQuantity(int,int)"/>: sets the copy count on every listed
    /// lot in one pass. Quantity isn't part of <see cref="BulkUpdateField"/> (that path copies
    /// identity/attributes, not quantity), so bulk edits route quantity through here.</summary>
    public void SetQuantity(IEnumerable<int> cardIds, int quantity)
    {
        using var context = _dbFactory.CreateDbContext();
        var ids = cardIds.ToList();
        var lots = context.Lots
            .Where(l => ids.Contains(l.Id) && l.Product.Category == ProductCategory.Single)
            .ToList();
        foreach (var lot in lots)
            lot.Quantity = Math.Max(1, quantity);
        context.SaveChanges();
    }

    public void DeleteCollectionCard(int id)
    {
        using var context = _dbFactory.CreateDbContext();
        var lot = context.Lots.FirstOrDefault(l => l.Id == id && l.Product.Category == ProductCategory.Single);
        if (lot is null)
            return;

        context.EbayListings.RemoveRange(context.EbayListings.Where(l => l.LotId == id));
        context.FlagResolutions.RemoveRange(context.FlagResolutions.Where(f => f.LotId == id));
        context.Lots.Remove(lot);
        context.SaveChanges();

        if (lot.ScanImagePath is not null)
        {
            var fullPath = Path.Combine(_dataPathService.DataDirectory, lot.ScanImagePath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
    }

    /// <summary>Places a card chosen from the catalog straight into a binder slot, swapping out any
    /// existing occupant (displaced to the Unplaced pool). Mirrors
    /// <c>CardService.AddMissingCardToSlot</c>.</summary>
    public void AddMissingCardToSlot(CardMatch match, CardGame game, string condition, bool isFoil, string? foilType, decimal? purchasePrice, int containerId, int page, int slot)
    {
        using var context = _dbFactory.CreateDbContext();

        var product = FindOrCreateProduct(context, game, match.GameSpecificId, isFoil, foilType,
            match.Name, match.SetCode, match.SetName, match.CollectorNumber, match.Rarity, match.ImageUri,
            CardAttributeExtractor.ExtractColor(match, game), CardAttributeExtractor.ExtractCardType(match, game));

        var occupant = context.Lots.FirstOrDefault(l =>
            l.LocationId == containerId && l.Page == page && l.Slot == slot);
        if (occupant is not null)
        {
            occupant.Page = null;
            occupant.Slot = null;
        }

        var lot = new InventoryLot
        {
            Product = product,
            Condition = condition,
            UnitCost = purchasePrice,
            LocationId = containerId,
            Page = page,
            Slot = slot,
        };
        context.Lots.Add(lot);
        context.SaveChanges();

        context.Movements.Add(new InventoryMovement
        {
            ProductId = lot.ProductId,
            LotId = lot.Id,
            Type = MovementType.Acquire,
            Quantity = 1,
            UnitValue = purchasePrice,
        });
        context.SaveChanges();
    }

    /// <summary>Moves one owned copy of <paramref name="lotId"/> into the binder pocket at
    /// (<paramref name="containerId"/>, <paramref name="page"/>, <paramref name="slot"/>). A single copy
    /// is split off a multi-copy lot so the remainder stays where it was; any card already in that pocket
    /// is displaced back to the Unplaced pool. Drives the binder editor's "Add card ▸ from your
    /// collection" flow (relocating a card from another location straight into a pocket). Mirrors the
    /// displace-then-place rules of <see cref="AddMissingCardToSlot"/> and the split rules of
    /// <c>CardService.MoveQuantityToContainer</c>.</summary>
    public void PlaceOwnedCardInSlot(int lotId, int containerId, int page, int slot)
    {
        using var context = _dbFactory.CreateDbContext();
        var lot = context.Lots.Include(l => l.Product)
            .FirstOrDefault(l => l.Id == lotId && l.Product.Category == ProductCategory.Single)
            ?? throw new InvalidOperationException($"Card {lotId} was not found in your collection.");

        // Hard block: a game-locked deck box rejects cards from other games (shared with the desktop path).
        DeckBoxGameGuard.ValidateIncoming(context, containerId, [lot.Product.Game]);

        // Displace any current occupant of the target pocket back to the Unplaced pool.
        var occupant = context.Lots.FirstOrDefault(l =>
            l.LocationId == containerId && l.Page == page && l.Slot == slot && l.Id != lotId);
        if (occupant is not null)
        {
            occupant.Page = null;
            occupant.Slot = null;
        }

        if (lot.Quantity <= 1)
        {
            // Whole-lot move into the pocket.
            lot.LocationId = containerId;
            lot.Page = page;
            lot.Slot = slot;
            lot.Section = null;
            context.SaveChanges();
            context.Movements.Add(new InventoryMovement
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                Type = MovementType.Move,
                Quantity = 1,
            });
            context.SaveChanges();
            return;
        }

        // Split one copy off the stack into a new single-copy lot placed in the pocket.
        lot.Quantity -= 1;
        var placed = new InventoryLot
        {
            ProductId = lot.ProductId,
            Quantity = 1,
            UnitCost = lot.UnitCost,
            AcquisitionDate = lot.AcquisitionDate,
            Source = lot.Source,
            Condition = lot.Condition,
            LocationId = containerId,
            Page = page,
            Slot = slot,
        };
        context.Lots.Add(placed);
        context.SaveChanges();

        context.Movements.Add(new InventoryMovement
        {
            ProductId = placed.ProductId,
            LotId = placed.Id,
            Type = MovementType.Move,
            Quantity = 1,
        });
        context.SaveChanges();
    }

    /// <summary>Splits <paramref name="quantity"/> copies off a stacked lot (Quantity &gt; 1) into a new
    /// loose sibling lot in the same container (no page/slot — it lands in the binder's Unplaced pool, so
    /// the user can then place each copy in its own pocket). Returns the new lot id, or 0 if the lot
    /// wasn't found. Throws if the lot is currently listed for sale (unlist first, since a listing's
    /// quantity is tied to the lot) or the split quantity isn't between 1 and Quantity-1.</summary>
    public int SplitStack(int lotId, int quantity)
    {
        if (quantity < 1)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Split quantity must be at least 1.");

        using var context = _dbFactory.CreateDbContext();
        var lot = context.Lots.Include(l => l.Product)
            .FirstOrDefault(l => l.Id == lotId && l.Product.Category == ProductCategory.Single);
        if (lot is null)
            return 0;
        if (quantity >= lot.Quantity)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Split quantity must be fewer than the stack's total.");

        // A listed lot's quantity is tied to its listing (and possibly an eBay multi-quantity listing),
        // so splitting it would desync those — require it to be unlisted first.
        if (context.Listings.Any(l => l.LotId == lotId
                && (l.Status == ListingStatus.Listed || l.Status == ListingStatus.Picked)))
            throw new InvalidOperationException("Unlist this card before splitting the stack.");

        lot.Quantity -= quantity;
        var split = new InventoryLot
        {
            ProductId = lot.ProductId,
            Quantity = quantity,
            UnitCost = lot.UnitCost,
            AcquisitionDate = lot.AcquisitionDate,
            Source = lot.Source,
            Condition = lot.Condition,
            LocationId = lot.LocationId,
            Section = lot.Section,
            // Page/Slot left null → the new copies land loose in the same container (Unplaced pool).
        };
        context.Lots.Add(split);
        context.SaveChanges();

        context.Movements.Add(new InventoryMovement
        {
            ProductId = split.ProductId,
            LotId = split.Id,
            Type = MovementType.Move,
            Quantity = quantity,
            Note = "Split from stack",
        });
        context.SaveChanges();
        return split.Id;
    }

    /// <summary>Relocates up to <paramref name="quantity"/> owned copies of <paramref name="lotId"/> into a
    /// location (no page/slot), splitting a larger stack so the remainder stays where it was. Returns the
    /// number of copies actually moved (0 if the lot no longer exists). Drives list-commit's "cards already
    /// in the collection are just moved to the chosen location" path — no new lot is created for these.</summary>
    public int MoveOwnedCopiesToContainer(int lotId, int quantity, int containerId)
    {
        using var context = _dbFactory.CreateDbContext();
        var lot = context.Lots.Include(l => l.Product)
            .FirstOrDefault(l => l.Id == lotId && l.Product.Category == ProductCategory.Single);
        if (lot is null)
            return 0;

        // Hard block: a game-locked deck box rejects cards from other games (shared with the desktop path).
        DeckBoxGameGuard.ValidateIncoming(context, containerId, [lot.Product.Game]);

        var move = Math.Clamp(quantity, 1, lot.Quantity);

        if (move >= lot.Quantity)
        {
            // Whole-lot relocation.
            lot.LocationId = containerId;
            lot.Page = null;
            lot.Slot = null;
            lot.Section = null;
            context.SaveChanges();
            context.Movements.Add(new InventoryMovement
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                Type = MovementType.Move,
                Quantity = move,
            });
            context.SaveChanges();
            return move;
        }

        // Split the moved copies off the stack into a new lot at the destination; the remainder stays put.
        lot.Quantity -= move;
        var moved = new InventoryLot
        {
            ProductId = lot.ProductId,
            Quantity = move,
            UnitCost = lot.UnitCost,
            AcquisitionDate = lot.AcquisitionDate,
            Source = lot.Source,
            Condition = lot.Condition,
            LocationId = containerId,
        };
        context.Lots.Add(moved);
        context.SaveChanges();

        context.Movements.Add(new InventoryMovement
        {
            ProductId = moved.ProductId,
            LotId = moved.Id,
            Type = MovementType.Move,
            Quantity = move,
        });
        context.SaveChanges();
        return move;
    }

    // --- Ported find-or-create-Product + identity/copy helpers (canonical copy: CardService.cs) ---

    private static Product FindOrCreateProduct(
        OmniCardDbContext context,
        Dictionary<(CardGame Game, string GameCardId, bool Foil, string? FoilType), Product> cache,
        CardGame game, string gameCardId, bool foil, string? foilType,
        string name, string? setCode, string? setName, string? number, string? rarity,
        string? imageUri, string? color, string? cardType)
    {
        if (!foil) foilType = null;

        var key = (game, gameCardId, foil, foilType);
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var product = context.Products.Local.FirstOrDefault(p =>
                p.Category == ProductCategory.Single && p.Game == game && p.GameCardId == gameCardId && p.Foil == foil && p.FoilType == foilType)
            ?? context.Products.FirstOrDefault(p =>
                p.Category == ProductCategory.Single && p.Game == game && p.GameCardId == gameCardId && p.Foil == foil && p.FoilType == foilType);

        if (product is null)
        {
            product = new Product
            {
                Game = game,
                Category = ProductCategory.Single,
                GameCardId = gameCardId,
                Foil = foil,
                FoilType = foilType,
                Name = name,
                SetCode = setCode,
                SetName = setName,
                CollectorNumber = number,
                Rarity = rarity,
                ImageUri = imageUri,
                Color = color,
                CardType = cardType,
            };
            context.Products.Add(product);
        }
        else
        {
            if (string.IsNullOrEmpty(product.Color) && !string.IsNullOrEmpty(color)) product.Color = color;
            if (string.IsNullOrEmpty(product.CardType) && !string.IsNullOrEmpty(cardType)) product.CardType = cardType;
            if (string.IsNullOrEmpty(product.ImageUri) && !string.IsNullOrEmpty(imageUri)) product.ImageUri = imageUri;
            if (string.IsNullOrEmpty(product.SetName) && !string.IsNullOrEmpty(setName)) product.SetName = setName;
            if (string.IsNullOrEmpty(product.Rarity) && !string.IsNullOrEmpty(rarity)) product.Rarity = rarity;
        }

        cache[key] = product;
        return product;
    }

    private static Product FindOrCreateProduct(
        OmniCardDbContext context,
        CardGame game, string gameCardId, bool foil, string? foilType,
        string name, string? setCode, string? setName, string? number, string? rarity,
        string? imageUri, string? color, string? cardType)
        => FindOrCreateProduct(context, [], game, gameCardId, foil, foilType, name, setCode, setName, number, rarity, imageUri, color, cardType);

    private static void ApplyIdentityAndCopyAttrs(OmniCardDbContext context, InventoryLot lot, CollectionCard card,
        Dictionary<(CardGame Game, string GameCardId, bool Foil, string? FoilType), Product>? productCache = null)
    {
        var product = lot.Product;
        var cardFoilType = card.IsFoil ? card.FoilType : null;
        var identityChanged =
            product.Game != card.Game ||
            (product.GameCardId ?? "") != card.GameCardId ||
            product.Foil != card.IsFoil ||
            product.FoilType != cardFoilType ||
            product.Name != card.Name ||
            (product.SetCode ?? "") != card.SetCode ||
            (product.SetName ?? "") != card.SetName ||
            (product.CollectorNumber ?? "") != card.Number ||
            (product.Rarity ?? "") != card.Rarity;

        if (identityChanged)
        {
            var target = FindOrCreateProduct(context, productCache ?? [], card.Game, card.GameCardId, card.IsFoil, cardFoilType,
                card.Name, card.SetCode, card.SetName, card.Number, card.Rarity, card.ImageUri, card.Color, card.CardType);
            lot.Product = target;
        }

        lot.Condition = card.Condition;
        lot.Note = card.Note;
        lot.UnitCost = card.PurchasePrice;
        lot.LocationId = card.ContainerId;
        lot.Page = card.Page;
        lot.Slot = card.Slot;
        lot.Section = card.Section;
        lot.ScanImagePath = card.ScanImagePath;
        lot.IsMissing = card.IsMissing;
        lot.FlagReason = card.FlagReason;
    }
}
