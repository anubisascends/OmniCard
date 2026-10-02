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
        // An item awaiting purchase has already had its owned copies moved out; keep new additions separate
        // so they're matched against the collection again.
        var existing = ctx.CardListItems.FirstOrDefault(i =>
            i.CardListId == listId && i.GameCardId == printing.GameSpecificId && i.IsFoil == isFoil && i.FoilType == foilType
            && !i.AwaitingPurchase && i.SubstituteForCardId == null);
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

    public bool ConsumeItems(int listId, IReadOnlyList<ListItemConsumption> consumed)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var items = ctx.CardListItems.Where(i => i.CardListId == listId).ToList();
        foreach (var c in consumed.Where(c => c.Quantity > 0).GroupBy(c => c.ItemId))
        {
            var item = items.FirstOrDefault(i => i.Id == c.Key);
            if (item is null) continue;
            item.Quantity -= c.Sum(x => x.Quantity);
            if (item.Quantity <= 0)
            {
                ctx.CardListItems.Remove(item);
                items.Remove(item);
            }
            else if (c.Any(x => x.MarkAwaitingPurchase))
            {
                item.AwaitingPurchase = true;
                item.SourceLotId = null;
            }
        }

        var deleted = items.Count == 0;
        if (deleted && ctx.CardLists.FirstOrDefault(l => l.Id == listId) is { } list)
            ctx.CardLists.Remove(list);
        ctx.SaveChanges();
        return deleted;
    }

    public void SetLanguage(int listId, string? language)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.FirstOrDefault(l => l.Id == listId);
        if (list is null) return;
        list.Language = CardLanguages.Normalize(language);
        ctx.SaveChanges();
    }

    public void SetSourceUrl(int listId, string? url)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.FirstOrDefault(l => l.Id == listId);
        if (list is null) return;
        list.SourceUrl = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        ctx.SaveChanges();
    }

    public ListUpdatePreview PreviewUpdate(int listId, string deckName, IEnumerable<DecklistEntry> entries)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.AsNoTracking().FirstOrDefault(l => l.Id == listId)
                   ?? throw new InvalidOperationException($"List {listId} not found.");
        var gs = cardService.GetGameService(list.Game);

        // The deck, resolved exactly as an import would resolve it (imports are always non-foil).
        var unresolved = new List<string>();
        var deck = new Dictionary<string, (CardMatch Printing, int Quantity)>();
        foreach (var entry in entries)
        {
            var printing = ResolvePrinting(gs, entry);
            if (printing is null) { unresolved.Add(entry.CardName); continue; }
            deck[printing.GameSpecificId] = deck.TryGetValue(printing.GameSpecificId, out var d)
                ? (d.Printing, d.Quantity + entry.Quantity)
                : (printing, entry.Quantity);
        }

        var groups = ctx.CardListItems.AsNoTracking()
            .Where(i => i.CardListId == listId)
            .AsEnumerable()
            .GroupBy(UpdateKey)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<ListUpdateRow>();
        var unchanged = 0;
        foreach (var (cardId, (printing, quantity)) in deck)
        {
            if (groups.TryGetValue((cardId, false), out var current))
            {
                var had = current.Sum(i => i.Quantity);
                if (had == quantity) { unchanged++; continue; }
                var first = current.FirstOrDefault(i => i.SubstituteForCardId == null) ?? current[0];
                rows.Add(new ListUpdateRow(ListUpdateKind.Change, cardId, printing.Name, printing.SetCode, printing.SetName,
                    printing.CollectorNumber, printing.Rarity, printing.ImageUri, false, had, quantity,
                    first.IsUnpriced ? null : first.AddedMarketPrice));
            }
            else
            {
                rows.Add(new ListUpdateRow(ListUpdateKind.Add, cardId, printing.Name, printing.SetCode, printing.SetName,
                    printing.CollectorNumber, printing.Rarity, printing.ImageUri, false, 0, quantity,
                    gs.GetCurrentPrice(cardId, isFoil: false)));
            }
        }

        foreach (var ((cardId, isFoil), current) in groups)
        {
            if (!isFoil && deck.ContainsKey(cardId)) continue;
            // A substitute's group is keyed by the card it replaces; show the replaced card's name.
            var first = current[0];
            rows.Add(new ListUpdateRow(ListUpdateKind.Remove, cardId, first.CardName,
                first.SubstituteForCardId is null ? first.SetCode : null, null,
                first.SubstituteForCardId is null ? first.CollectorNumber : null, null, null, isFoil,
                current.Sum(i => i.Quantity), 0, first.IsUnpriced ? null : first.AddedMarketPrice,
                HandAdded: current.All(i => i.Source != ListItemSource.Url)));
        }

        return new ListUpdatePreview(deckName,
            rows.OrderBy(r => r.Kind).ThenBy(r => r.CardName, StringComparer.OrdinalIgnoreCase).ToList(),
            unchanged, unresolved);
    }

    public void ApplyUpdate(int listId, IReadOnlyList<ListUpdateRow> approved)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        if (!ctx.CardLists.Any(l => l.Id == listId))
            throw new InvalidOperationException($"List {listId} not found.");
        var groups = ctx.CardListItems
            .Where(i => i.CardListId == listId)
            .AsEnumerable()
            .GroupBy(UpdateKey)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var row in approved.Where(r => !string.IsNullOrEmpty(r.GameCardId)))
        {
            var items = groups.GetValueOrDefault((row.GameCardId, row.IsFoil)) ?? [];
            var target = Math.Max(0, row.NewQuantity);
            var diff = target - items.Sum(i => i.Quantity);

            if (diff > 0)
            {
                var grow = items.FirstOrDefault(i => !i.AwaitingPurchase && i.SubstituteForCardId == null);
                if (grow is not null)
                {
                    grow.Quantity += diff;
                    continue;
                }
                ctx.CardListItems.Add(new CardListItem
                {
                    CardListId = listId,
                    Quantity = diff,
                    GameCardId = row.GameCardId,
                    CardName = row.CardName,
                    SetCode = string.IsNullOrEmpty(row.SetCode) ? null : row.SetCode,
                    CollectorNumber = string.IsNullOrEmpty(row.CollectorNumber) ? null : row.CollectorNumber,
                    IsFoil = row.IsFoil,
                    AddedMarketPrice = row.Price,
                    IsUnpriced = row.Price is null,
                    Source = ListItemSource.Url,
                });
            }
            else if (diff < 0)
            {
                // Shrink what's still to buy first, then the card itself, and stand-ins last.
                var shrink = -diff;
                foreach (var item in items
                             .OrderBy(i => i.SubstituteForCardId != null)
                             .ThenByDescending(i => i.AwaitingPurchase))
                {
                    if (shrink == 0) break;
                    var take = Math.Min(item.Quantity, shrink);
                    item.Quantity -= take;
                    shrink -= take;
                    if (item.Quantity == 0) ctx.CardListItems.Remove(item);
                }
            }
        }
        ctx.SaveChanges();
    }

    public void ApplySubstitutions(int listId, IReadOnlyList<ListSubstitution> substitutions)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.AsNoTracking().FirstOrDefault(l => l.Id == listId)
                   ?? throw new InvalidOperationException($"List {listId} not found.");
        var gs = cardService.GetGameService(list.Game);
        var items = ctx.CardListItems.Where(i => i.CardListId == listId).ToList();
        var lotIds = substitutions.Select(s => s.LotId).Distinct().ToList();
        var lots = ctx.Lots.AsNoTracking().Include(l => l.Product)
            .Where(l => lotIds.Contains(l.Id))
            .ToDictionary(l => l.Id);

        foreach (var sub in substitutions)
        {
            var item = items.FirstOrDefault(i => i.Id == sub.ItemId);
            if (item is null || !lots.TryGetValue(sub.LotId, out var lot) || lot.Product.Game != list.Game) continue;
            var quantity = Math.Min(Math.Min(sub.Quantity, item.Quantity), Math.Max(lot.Quantity, 1));
            if (quantity < 1) continue;

            var replaces = item.SubstituteForCardId ?? item.GameCardId;
            var p = lot.Product;
            var existing = items.FirstOrDefault(i => i.SourceLotId == lot.Id && i.SubstituteForCardId == replaces);
            if (existing is not null)
            {
                existing.Quantity += quantity;
            }
            else
            {
                var price = gs.GetCurrentPrice(p.GameCardId ?? "", p.Foil);
                var added = new CardListItem
                {
                    CardListId = listId,
                    Quantity = quantity,
                    GameCardId = p.GameCardId ?? "",
                    CardName = p.Name,
                    SetCode = string.IsNullOrEmpty(p.SetCode) ? null : p.SetCode,
                    CollectorNumber = string.IsNullOrEmpty(p.CollectorNumber) ? null : p.CollectorNumber,
                    IsFoil = p.Foil,
                    FoilType = p.Foil ? p.FoilType : null,
                    AddedMarketPrice = price,
                    IsUnpriced = price is null,
                    SourceLotId = lot.Id,
                    SubstituteForCardId = replaces,
                    Source = ListItemSource.Manual,
                };
                ctx.CardListItems.Add(added);
                items.Add(added);
            }

            item.Quantity -= quantity;
            if (item.Quantity <= 0)
            {
                ctx.CardListItems.Remove(item);
                items.Remove(item);
            }
        }
        ctx.SaveChanges();
    }

    /// <summary>The printing an item counts toward when comparing with its source deck: a stand-in counts
    /// toward the (non-foil, as imported) card it replaces.</summary>
    private static (string CardId, bool IsFoil) UpdateKey(CardListItem i) =>
        i.SubstituteForCardId is { } replaces ? (replaces, false) : (i.GameCardId, i.IsFoil);

    /// <summary>Honors the exact printing (set + collector number) the entry specifies — e.g. the printing a
    /// Moxfield/Archidekt URL points at — falling back to the cheapest printing by name so a card is never
    /// dropped just because its printing couldn't be located.</summary>
    private static CardMatch? ResolvePrinting(ICardGameService gs, DecklistEntry entry) =>
        DecklistPrintingResolver.Resolve(gs, entry) ?? ResolveCheapest(gs, entry.CardName)?.Printing;

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
            var printing = ResolvePrinting(gs, entry);
            if (printing is null) { unresolved.Add(entry.CardName); continue; }
            var price = gs.GetCurrentPrice(printing.GameSpecificId, isFoil: false);

            var existing = pendingByGameCardId.TryGetValue(printing.GameSpecificId, out var pending)
                ? pending
                : ctx.CardListItems.FirstOrDefault(i =>
                    i.CardListId == listId && i.GameCardId == printing.GameSpecificId && !i.IsFoil && !i.AwaitingPurchase
                    && i.SubstituteForCardId == null);
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
