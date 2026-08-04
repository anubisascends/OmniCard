using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public sealed class TradeService(IDbContextFactory<OmniCardDbContext> dbContextFactory) : ITradeService
{
    public List<TradeSummary> GetTrades()
    {
        using var context = dbContextFactory.CreateDbContext();
        var replacementCounts = context.Lots.AsNoTracking()
            .Where(l => l.FulfilledTradeId != null)
            .GroupBy(l => l.FulfilledTradeId!.Value)
            .Select(g => new { TradeId = g.Key, Count = g.Count() })
            .ToDictionary(x => x.TradeId, x => x.Count);

        return context.Trades.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToList()
            .Select(t => ToSummary(t, replacementCounts.GetValueOrDefault(t.Id)))
            .ToList();
    }

    public TradeSummary? GetTrade(int id)
    {
        using var context = dbContextFactory.CreateDbContext();
        var trade = context.Trades.AsNoTracking().FirstOrDefault(t => t.Id == id);
        if (trade is null) return null;

        var replacementCount = context.Lots.AsNoTracking().Count(l => l.FulfilledTradeId == id);
        return ToSummary(trade, replacementCount);
    }

    private static TradeSummary ToSummary(Trade trade, int replacementCount) => new()
    {
        Id = trade.Id,
        Game = trade.Game,
        CardName = trade.CardName,
        SetCode = trade.SetCode,
        SetName = trade.SetName,
        CollectorNumber = trade.CollectorNumber,
        Foil = trade.Foil,
        Note = trade.Note,
        PhotoPath = trade.PhotoPath,
        CreatedAt = trade.CreatedAt,
        ReplacementCount = replacementCount,
    };
}
