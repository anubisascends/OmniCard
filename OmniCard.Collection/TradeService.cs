using System.IO;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public sealed class TradeService(
    IDbContextFactory<OmniCardDbContext> dbContextFactory,
    IDataPathService dataPathService) : ITradeService
{
    public List<TradeSummary> GetTrades()
    {
        using var context = dbContextFactory.CreateDbContext();

        var replacementCounts = context.Lots.AsNoTracking()
            .Where(l => l.FulfilledTradeSessionId != null)
            .GroupBy(l => l.FulfilledTradeSessionId!.Value)
            .Select(g => new { SessionId = g.Key, Count = g.Count() })
            .ToDictionary(x => x.SessionId, x => x.Count);

        var sessions = context.TradeSessions.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .ToList();

        var trades = context.Trades.AsNoTracking()
            .Where(t => t.TradeSessionId != null)
            .ToList()
            .ToLookup(t => t.TradeSessionId!.Value);

        // Traded-away lots are deleted on fulfillment, so a surviving lot's scan is only sometimes
        // available; look up scan paths for the outgoing lots still present.
        var lotScanPaths = context.Lots.AsNoTracking()
            .Where(l => l.ScanImagePath != null)
            .ToDictionary(l => l.Id, l => ResolveScanPath(l.ScanImagePath));

        return sessions
            .Select(s => ToSummary(s, trades[s.Id], replacementCounts.GetValueOrDefault(s.Id), lotScanPaths))
            .ToList();
    }

    public TradeSummary? GetTrade(int id)
    {
        using var context = dbContextFactory.CreateDbContext();
        var session = context.TradeSessions.AsNoTracking().FirstOrDefault(s => s.Id == id);
        if (session is null) return null;

        var trades = context.Trades.AsNoTracking().Where(t => t.TradeSessionId == id).ToList();
        var replacementCount = context.Lots.AsNoTracking().Count(l => l.FulfilledTradeSessionId == id);
        var lotScanPaths = context.Lots.AsNoTracking()
            .Where(l => l.ScanImagePath != null)
            .ToDictionary(l => l.Id, l => ResolveScanPath(l.ScanImagePath));

        return ToSummary(session, trades, replacementCount, lotScanPaths);
    }

    /// <summary>Scan paths are stored relative to the data directory (e.g. "scans/123.jpg"); the
    /// image converters need an absolute path.</summary>
    private string? ResolveScanPath(string? scanImagePath) =>
        string.IsNullOrEmpty(scanImagePath)
            ? null
            : Path.Combine(dataPathService.DataDirectory, scanImagePath);

    private static TradeSummary ToSummary(
        TradeSession session,
        IEnumerable<Trade> trades,
        int replacementCount,
        IReadOnlyDictionary<int, string?> lotScanPaths) => new()
    {
        Id = session.Id,
        Note = session.Note,
        ReceivedPhotoPath = session.ReceivedPhotoPath,
        OutgoingValue = session.OutgoingValue,
        ReceivedValue = session.ReceivedValue,
        CreatedAt = session.CreatedAt,
        FirstFulfilledAt = session.FirstFulfilledAt,
        ReplacementCount = replacementCount,
        OutgoingCards = trades
            .OrderBy(t => t.Id)
            .Select(t => new TradeCardSummary
            {
                Game = t.Game,
                CardName = t.CardName,
                SetCode = t.SetCode,
                SetName = t.SetName,
                CollectorNumber = t.CollectorNumber,
                Foil = t.Foil,
                IsOffDatabase = t.IsOffDatabase,
                EstimatedValue = t.EstimatedValue,
                PhotoPath = t.OffDbPhotoPath
                    ?? (t.OriginalLotId is int lotId ? lotScanPaths.GetValueOrDefault(lotId) : null),
            })
            .ToList(),
    };
}
