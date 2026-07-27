using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

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

    public CardListItem AddPrinting(int listId, CardMatch printing, bool isFoil, int quantity, ListItemSource source)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.AsNoTracking().FirstOrDefault(l => l.Id == listId)
                   ?? throw new InvalidOperationException($"List {listId} not found.");

        var existing = ctx.CardListItems.FirstOrDefault(i =>
            i.CardListId == listId && i.GameCardId == printing.GameSpecificId && i.IsFoil == isFoil);
        if (existing is not null)
        {
            existing.Quantity += quantity;
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
            AddedMarketPrice = price,
            IsUnpriced = price is null,
            Source = source,
        };
        ctx.CardListItems.Add(item);
        ctx.SaveChanges();
        return item;
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

    // ---- Task 3 implements these ----
    public AddCardsResult AddCardsByName(int listId, IEnumerable<DecklistEntry> entries)
        => throw new NotImplementedException();
    public void RefreshPrices(int listId) => throw new NotImplementedException();
    public List<DecklistEntry> ToDecklistEntries(int listId) => throw new NotImplementedException();
}
