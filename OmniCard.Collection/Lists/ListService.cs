using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Storage;

namespace OmniCard.Collection.Lists;

public class ListService(
    IDbContextFactory<OmniCardDbContext> dbContextFactory,
    ICardService cardService) : IListService
{
    public IReadOnlyList<CardList> GetLists(CardGame game)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.CardLists.AsNoTracking()
            .Where(l => l.Game == game)
            .OrderBy(l => l.Name)
            .ToList();
    }

    public CardList CreateList(string name, CardGame game)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = new CardList { Name = name, Game = game, CreatedUtc = DateTime.UtcNow };
        ctx.CardLists.Add(list);
        ctx.SaveChanges();
        return list;
    }

    public void RenameList(int listId, string name)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.FirstOrDefault(l => l.Id == listId);
        if (list is null) return;
        list.Name = name;
        ctx.SaveChanges();
    }

    public void DeleteList(int listId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.FirstOrDefault(l => l.Id == listId);
        if (list is null) return;
        var items = ctx.CardListItems.Where(i => i.CardListId == listId).ToList();
        ctx.CardListItems.RemoveRange(items);
        ctx.CardLists.Remove(list);
        ctx.SaveChanges();
    }

    public IReadOnlyList<CardListItem> GetItems(int listId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.CardListItems.AsNoTracking()
            .Where(i => i.CardListId == listId)
            .OrderBy(i => i.CardName)
            .ToList();
    }

    public CardListItem AddPrinting(int listId, CardMatch printing, bool isFoil, string? foilType, int quantity, ListItemSource source, int? sourceLotId = null)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.AsNoTracking().FirstOrDefault(l => l.Id == listId)
                   ?? throw new InvalidOperationException($"List {listId} not found.");

        if (!isFoil) foilType = null;
        var existing = ctx.CardListItems.FirstOrDefault(i =>
            i.CardListId == listId && i.GameCardId == printing.GameSpecificId && i.IsFoil == isFoil && i.FoilType == foilType);
        if (existing is not null)
        {
            existing.Quantity += quantity;
            // Adopt the owned-copy reference if this item didn't already carry one, so a later commit
            // relocates the physical copy rather than minting a duplicate.
            if (existing.SourceLotId is null && sourceLotId is not null)
                existing.SourceLotId = sourceLotId;
            ctx.SaveChanges();
            return existing;
        }

        var price = cardService.GetGameService(list.Game).GetCurrentPrice(printing.GameSpecificId, isFoil);
        var item = new CardListItem
        {
            CardListId = listId,
            Quantity = quantity,
            GameCardId = printing.GameSpecificId,
            CardName = printing.Name,
            SetCode = string.IsNullOrEmpty(printing.SetCode) ? null : printing.SetCode,
            CollectorNumber = string.IsNullOrEmpty(printing.CollectorNumber) ? null : printing.CollectorNumber,
            IsFoil = isFoil,
            FoilType = foilType,
            AddedMarketPrice = price,
            IsUnpriced = price is null,
            SourceLotId = sourceLotId,
            Source = source,
        };
        ctx.CardListItems.Add(item);
        ctx.SaveChanges();
        return item;
    }

    public CardListItem AddOwnedLot(int listId, int lotId, int quantity)
    {
        CardMatch match;
        bool isFoil;
        string? foilType;
        using (var ctx = dbContextFactory.CreateDbContext())
        {
            var lot = ctx.Lots.AsNoTracking().Include(l => l.Product)
                          .FirstOrDefault(l => l.Id == lotId)
                      ?? throw new InvalidOperationException($"Card {lotId} was not found in your collection.");
            var p = lot.Product;
            isFoil = p.Foil;
            foilType = p.FoilType;
            match = new CardMatch
            {
                GameSpecificId = p.GameCardId ?? "",
                Name = p.Name,
                SetCode = p.SetCode ?? "",
                SetName = p.SetName ?? "",
                CollectorNumber = p.CollectorNumber ?? "",
                Rarity = p.Rarity ?? "",
                ImageUri = p.ImageUri,
            };
        }
        // Adding to a list is a pure reference — the source lot is untouched here. Stamp Manual so
        // RefreshPrices keeps this exact printing rather than re-tracking the cheapest one.
        return AddPrinting(listId, match, isFoil, foilType, Math.Max(1, quantity), ListItemSource.Manual, lotId);
    }

    public void RemoveItem(int itemId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var item = ctx.CardListItems.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return;
        ctx.CardListItems.Remove(item);
        ctx.SaveChanges();
    }

    public void SetQuantity(int itemId, int quantity)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var item = ctx.CardListItems.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return;
        if (quantity <= 0) { ctx.CardListItems.Remove(item); }
        else { item.Quantity = quantity; }
        ctx.SaveChanges();
    }

    public AddCardsResult AddCardsByName(int listId, IEnumerable<DecklistEntry> entries, ListItemSource source = ListItemSource.Paste)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.AsNoTracking().FirstOrDefault(l => l.Id == listId)
                   ?? throw new InvalidOperationException($"List {listId} not found.");
        var gs = cardService.GetGameService(list.Game);

        var unresolved = new List<string>();
        var added = 0;
        // Tracks items added earlier in this same call: a plain query wouldn't see them
        // until SaveChanges, so repeated names within one call would otherwise duplicate rows.
        var pendingByGameCardId = new Dictionary<string, CardListItem>();

        foreach (var entry in entries)
        {
            // Honor the exact printing (set + collector number) the entry specifies — e.g. the printing a
            // Moxfield/Archidekt URL points at — instead of collapsing to the cheapest printing of the name.
            // Fall back to cheapest-by-name so a card is never dropped just because its printing couldn't be
            // located (a name-only line, or an exact set/collector that isn't in the catalog).
            var printing = DecklistPrintingResolver.Resolve(gs, entry) ?? ResolveCheapest(gs, entry.CardName)?.Printing;
            if (printing is null) { unresolved.Add(entry.CardName); continue; }
            var price = gs.GetCurrentPrice(printing.GameSpecificId, isFoil: false);

            var existing = pendingByGameCardId.TryGetValue(printing.GameSpecificId, out var pending)
                ? pending
                : ctx.CardListItems.FirstOrDefault(i =>
                    i.CardListId == listId && i.GameCardId == printing.GameSpecificId && !i.IsFoil);
            if (existing is not null)
            {
                existing.Quantity += entry.Quantity;
            }
            else
            {
                var newItem = new CardListItem
                {
                    CardListId = listId,
                    Quantity = entry.Quantity,
                    GameCardId = printing.GameSpecificId,
                    CardName = printing.Name,
                    SetCode = string.IsNullOrEmpty(printing.SetCode) ? null : printing.SetCode,
                    CollectorNumber = string.IsNullOrEmpty(printing.CollectorNumber) ? null : printing.CollectorNumber,
                    IsFoil = false,
                    AddedMarketPrice = price,
                    IsUnpriced = price is null,
                    Source = source,
                };
                ctx.CardListItems.Add(newItem);
                pendingByGameCardId[printing.GameSpecificId] = newItem;
            }
            added += entry.Quantity;
        }

        ctx.SaveChanges();
        return new AddCardsResult(added, unresolved);
    }

    public void RefreshPrices(int listId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.AsNoTracking().FirstOrDefault(l => l.Id == listId);
        if (list is null) return;
        var gs = cardService.GetGameService(list.Game);

        foreach (var item in ctx.CardListItems.Where(i => i.CardListId == listId).ToList())
        {
            // Manual and URL-imported items point at a deliberately chosen printing (a URL import freezes the
            // exact set + collector from the deck), so only reprice them — never swap the printing. Name-only
            // sources (paste/file/scan) re-track the current cheapest printing of the name.
            if (item.Source is ListItemSource.Manual or ListItemSource.Url)
            {
                item.AddedMarketPrice = gs.GetCurrentPrice(item.GameCardId, item.IsFoil);
                item.IsUnpriced = item.AddedMarketPrice is null;
            }
            else
            {
                var resolved = ResolveCheapest(gs, item.CardName);
                if (resolved is null) continue; // leave as-is if no longer resolvable
                var (printing, price, unpriced) = resolved.Value;
                item.GameCardId = printing.GameSpecificId;
                item.SetCode = string.IsNullOrEmpty(printing.SetCode) ? null : printing.SetCode;
                item.CollectorNumber = string.IsNullOrEmpty(printing.CollectorNumber) ? null : printing.CollectorNumber;
                item.AddedMarketPrice = price;
                item.IsUnpriced = unpriced;
            }
        }
        ctx.SaveChanges();
    }

    public List<DecklistEntry> ToDecklistEntries(int listId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.CardListItems.AsNoTracking()
            .Where(i => i.CardListId == listId)
            .AsEnumerable()
            .Select(i => new DecklistEntry(i.Quantity, i.CardName, i.SetCode, i.CollectorNumber))
            .ToList();
    }

    public CommitToLocationResult CommitToLocation(int listId, StorageContainer container, string condition)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.FirstOrDefault(l => l.Id == listId)
                   ?? throw new InvalidOperationException($"List {listId} not found.");
        var gs = cardService.GetGameService(list.Game);
        var items = ctx.CardListItems.Where(i => i.CardListId == listId).ToList();

        var added = 0;
        var unresolved = 0;
        foreach (var item in items)
        {
            var entry = new DecklistEntry(item.Quantity, item.CardName, item.SetCode, item.CollectorNumber);
            var match = DecklistPrintingResolver.Resolve(gs, entry);
            if (match is null)
            {
                unresolved++;
                continue;
            }

            cardService.AddCardToCollection(match, list.Game, condition, item.IsFoil, item.FoilType,
                purchasePrice: null, quantity: item.Quantity, container, page: null, slot: null, section: null);
            added += item.Quantity;
            ctx.CardListItems.Remove(item);
        }

        var deleted = false;
        if (unresolved == 0)
        {
            ctx.CardLists.Remove(list);
            deleted = true;
        }

        ctx.SaveChanges();
        return new CommitToLocationResult(added, unresolved, deleted);
    }

    /// <summary>Cheapest non-foil printing of the named card. Returns null if no printing exists;
    /// on no-price, returns the first printing flagged unpriced.</summary>
    private static (CardMatch Printing, decimal? Price, bool Unpriced)? ResolveCheapest(
        ICardGameService gs, string cardName)
    {
        var printings = DecklistPrintingResolver.GetPrintingsFuzzy(gs, cardName);
        if (printings.Count == 0) return null;

        var prices = gs.GetCurrentPrices(printings.Select(p => p.GameSpecificId), isFoil: false);
        var priced = printings
            .Where(p => prices.ContainsKey(p.GameSpecificId))
            .OrderBy(p => prices[p.GameSpecificId])
            .ToList();

        if (priced.Count > 0)
            return (priced[0], prices[priced[0].GameSpecificId], false);
        return (printings[0], null, true);
    }
}
