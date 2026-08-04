using System.IO;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public sealed class TradeImportService(
    IDbContextFactory<OmniCardDbContext> dbContextFactory,
    IDataPathService dataPathService,
    ILogger<TradeImportService> logger) : ITradeImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int ImportPendingTrades()
    {
        var tradesDir = dataPathService.TradesDirectory;
        if (!Directory.Exists(tradesDir))
            return 0;

        var imported = 0;
        foreach (var folder in Directory.GetDirectories(tradesDir))
        {
            var jsonPath = Path.Combine(folder, "trade.json");
            if (!File.Exists(jsonPath))
                continue;

            TradeRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<TradeRecord>(File.ReadAllText(jsonPath), JsonOptions);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read trade record at {Path}", jsonPath);
                continue;
            }

            if (record is null)
                continue;

            if (record.ProcessedAt is not null)
            {
                // Already applied — but if this record predates the Trade table (or the DB row
                // was otherwise never created), backfill it now without reapplying anything else.
                BackfillTradeRowIfMissing(record, folder);
                continue;
            }

            try
            {
                ApplyTrade(record, folder);
                if (record.ProcessingError is null)
                    imported++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to apply trade {TradeId}", record.TradeId);
                record.ProcessingError = ex.Message;
            }

            // Marked processed regardless of outcome — an unresolvable trade (bad data, missing
            // lot, DB error) would otherwise retry and fail identically on every future launch.
            record.ProcessedAt = DateTime.UtcNow;
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(record, JsonOptions));
        }

        return imported;
    }

    private void ApplyTrade(TradeRecord record, string folder)
    {
        using var context = dbContextFactory.CreateDbContext();
        var lot = context.Lots.FirstOrDefault(l => l.Id == record.LotId);
        if (lot is null)
        {
            record.ProcessingError = $"Lot {record.LotId} no longer exists.";
            logger.LogWarning("Trade {TradeId} references missing lot {LotId}", record.TradeId, record.LotId);
            return;
        }

        var photoPath = string.IsNullOrEmpty(record.PhotoFileName)
            ? null
            : Path.Combine(folder, record.PhotoFileName);

        lot.IsTraded = true;
        lot.TradeNote = record.Note;
        lot.TradePhotoPath = photoPath;

        context.Movements.Add(new InventoryMovement
        {
            ProductId = lot.ProductId,
            LotId = lot.Id,
            Type = MovementType.Trade,
            Quantity = 1,
            Note = record.Note,
        });

        // Permanent trade record — independent of the lot, which gets deleted once a linked
        // replacement scan is committed (see CardService.CommitScans).
        context.Trades.Add(new Trade
        {
            TradeRecordId = record.TradeId,
            Game = record.Game,
            CardName = record.CardName,
            SetCode = record.SetCode,
            SetName = record.SetName,
            CollectorNumber = record.CollectorNumber,
            Foil = record.Foil,
            Note = record.Note,
            PhotoPath = photoPath,
            OriginalLotId = lot.Id,
            CreatedAt = record.CreatedAt,
            ImportedAt = DateTime.UtcNow,
        });

        context.SaveChanges();
        logger.LogInformation("Applied trade {TradeId} to lot {LotId}", record.TradeId, record.LotId);
    }

    /// <summary>Creates the permanent Trade row for a trade.json that was already marked
    /// processed before the Trade table existed (or the DB row is otherwise missing) — a no-op
    /// once the row exists. Records from before the card-identity snapshot was added to
    /// TradeRecord (empty CardName) fall back to the live Lot/Product, which is still faithful
    /// as long as the lot hasn't been fulfilled/deleted yet.</summary>
    private void BackfillTradeRowIfMissing(TradeRecord record, string folder)
    {
        using var context = dbContextFactory.CreateDbContext();
        if (context.Trades.Any(t => t.TradeRecordId == record.TradeId))
            return;

        var lot = context.Lots.Include(l => l.Product).FirstOrDefault(l => l.Id == record.LotId);
        var hasSnapshot = !string.IsNullOrEmpty(record.CardName);

        var trade = new Trade
        {
            TradeRecordId = record.TradeId,
            Note = record.Note,
            PhotoPath = string.IsNullOrEmpty(record.PhotoFileName) ? null : Path.Combine(folder, record.PhotoFileName),
            CreatedAt = record.CreatedAt,
            ImportedAt = DateTime.UtcNow,
            OriginalLotId = lot?.Id,
            Game = hasSnapshot ? record.Game : lot?.Product.Game ?? default,
            CardName = hasSnapshot ? record.CardName : lot?.Product.Name ?? "(unknown)",
            SetCode = hasSnapshot ? record.SetCode : lot?.Product.SetCode,
            SetName = hasSnapshot ? record.SetName : lot?.Product.SetName,
            CollectorNumber = hasSnapshot ? record.CollectorNumber : lot?.Product.CollectorNumber,
            Foil = hasSnapshot ? record.Foil : lot?.Product.Foil ?? false,
        };

        context.Trades.Add(trade);
        context.SaveChanges();
        logger.LogInformation("Backfilled Trade row for pre-existing trade {TradeId}", record.TradeId);
    }
}
