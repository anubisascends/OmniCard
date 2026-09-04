using System.IO;
using Microsoft.EntityFrameworkCore;
using OmniCard.CardMatching;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

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

    // Takes the factory interface (not the concrete writable type) so it's unit-testable with an
    // in-memory factory. In production it's constructed explicitly in Program.cs with the writable
    // factory — never via container constructor injection — so it can't accidentally bind to the
    // app's read-only IDbContextFactory<OmniCardDbContext>.
    public WebBinderCardService(
        IDbContextFactory<OmniCardDbContext> dbFactory,
        IDataPathService dataPathService)
    {
        _dbFactory = dbFactory;
        _dataPathService = dataPathService;
    }

    /// <summary>Cards in the binder that have no page assignment (the "Unplaced pool"), narrowed by
    /// the optional Scryfall-syntax filter. Mirrors <c>CardService.GetUnplacedBinderCards</c>.</summary>
    public List<CollectionCard> GetUnplacedBinderCards(int containerId, FilterPreset? filterPreset)
    {
        using var context = _dbFactory.CreateDbContext();
        return CollectionQueryBuilder.BuildFilteredQuery(context, "", null, containerId, filterPreset)
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

    public void MoveCardsToContainer(IEnumerable<int> cardIds, int containerId, string? section = null)
    {
        using var context = _dbFactory.CreateDbContext();
        var ids = cardIds.ToList();
        var lots = context.Lots.Where(l => ids.Contains(l.Id) && l.Product.Category == ProductCategory.Single).ToList();
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
