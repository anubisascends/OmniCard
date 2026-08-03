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

            if (record is null || record.ProcessedAt is not null)
                continue;

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

        lot.IsTraded = true;
        lot.TradeNote = record.Note;
        lot.TradePhotoPath = string.IsNullOrEmpty(record.PhotoFileName)
            ? null
            : Path.Combine(folder, record.PhotoFileName);

        context.Movements.Add(new InventoryMovement
        {
            ProductId = lot.ProductId,
            LotId = lot.Id,
            Type = MovementType.Trade,
            Quantity = 1,
            Note = record.Note,
        });

        context.SaveChanges();
        logger.LogInformation("Applied trade {TradeId} to lot {LotId}", record.TradeId, record.LotId);
    }
}
