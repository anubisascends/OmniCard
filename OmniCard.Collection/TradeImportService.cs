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

            string text;
            try
            {
                text = File.ReadAllText(jsonPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read trade record at {Path}", jsonPath);
                continue;
            }

            try
            {
                imported += SchemaVersionOf(text) >= 2
                    ? ProcessSessionFolder(text, jsonPath, folder)
                    : ProcessLegacyFolder(text, jsonPath, folder);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process trade folder {Folder}", folder);
            }
        }

        return imported;
    }

    /// <summary>Reads just the SchemaVersion field. Legacy single-card records
    /// (<see cref="TradeRecord"/>) have no such field, so they read as 1.</summary>
    private static int SchemaVersionOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("SchemaVersion", out var v)
                   && v.ValueKind == JsonValueKind.Number
                ? v.GetInt32()
                : 1;
        }
        catch
        {
            return 1;
        }
    }

    // ---- Schema v2: multi-card trade sessions ---------------------------------------------------

    private int ProcessSessionFolder(string text, string jsonPath, string folder)
    {
        var record = JsonSerializer.Deserialize<TradeSessionRecord>(text, JsonOptions);
        if (record is null)
            return 0;

        // Drafts are still being built on the web app — never apply, never stamp.
        if (!string.Equals(record.Status, "final", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (record.ProcessedAt is not null)
        {
            BackfillSessionIfMissing(record, folder);
            return 0;
        }

        var imported = 0;
        try
        {
            ApplySession(record, folder);
            if (record.ProcessingError is null)
                imported = 1;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to apply trade session {SessionId}", record.SessionId);
            record.ProcessingError = ex.Message;
        }

        record.ProcessedAt = DateTime.UtcNow;
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(record, JsonOptions));
        return imported;
    }

    private void ApplySession(TradeSessionRecord record, string folder)
    {
        using var context = dbContextFactory.CreateDbContext();
        if (context.TradeSessions.Any(s => s.SessionRecordId == record.SessionId))
            return; // already applied

        var receivedPhoto = string.IsNullOrEmpty(record.ReceivedPhotoFileName)
            ? null
            : Path.Combine(folder, record.ReceivedPhotoFileName);

        var session = new TradeSession
        {
            SessionRecordId = record.SessionId,
            Note = record.Note,
            ReceivedPhotoPath = receivedPhoto,
            ReceivedValue = record.ReceivedValue,
            CreatedAt = record.CreatedAt,
            ImportedAt = DateTime.UtcNow,
        };
        context.TradeSessions.Add(session);
        context.SaveChanges(); // materialize session.Id for the child Trade rows

        decimal outgoingValue = 0m;
        foreach (var item in record.OutgoingItems)
        {
            var trade = new Trade
            {
                TradeSessionId = session.Id,
                TradeRecordId = record.SessionId,
                Game = item.Game,
                CardName = item.CardName,
                SetCode = item.SetCode,
                SetName = item.SetName,
                CollectorNumber = item.CollectorNumber,
                Foil = item.Foil,
                IsOffDatabase = item.IsOffDatabase,
                EstimatedValue = item.EstimatedValue,
                CreatedAt = record.CreatedAt,
                ImportedAt = DateTime.UtcNow,
            };

            if (item.IsOffDatabase)
            {
                trade.OffDbPhotoPath = string.IsNullOrEmpty(item.PhotoFileName)
                    ? null
                    : Path.Combine(folder, item.PhotoFileName);
            }
            else if (item.LotId is int lotId)
            {
                var lot = context.Lots.FirstOrDefault(l => l.Id == lotId);
                if (lot is null)
                {
                    logger.LogWarning("Trade session {SessionId} references missing lot {LotId}; " +
                        "recording the card without a lot.", record.SessionId, lotId);
                }
                else
                {
                    lot.IsTraded = true;
                    lot.TradeNote = record.Note;
                    lot.TradePhotoPath = receivedPhoto;
                    context.Movements.Add(new InventoryMovement
                    {
                        ProductId = lot.ProductId,
                        LotId = lot.Id,
                        Type = MovementType.Trade,
                        Quantity = 1,
                        Note = record.Note,
                    });
                    trade.OriginalLotId = lot.Id;
                }
            }

            outgoingValue += item.EstimatedValue ?? 0m;
            context.Trades.Add(trade);
        }

        session.OutgoingValue = outgoingValue;
        context.SaveChanges();
        logger.LogInformation("Applied trade session {SessionId} with {Count} outgoing card(s)",
            record.SessionId, record.OutgoingItems.Count);
    }

    /// <summary>Rebuilds the session + Trade rows for a v2 record already marked processed but with
    /// no session row (e.g. a DB restored from before this table existed). Reconstructs history
    /// only — it does not re-mark lots or add movements, which were applied on first processing.</summary>
    private void BackfillSessionIfMissing(TradeSessionRecord record, string folder)
    {
        using var context = dbContextFactory.CreateDbContext();
        if (context.TradeSessions.Any(s => s.SessionRecordId == record.SessionId))
            return;

        var receivedPhoto = string.IsNullOrEmpty(record.ReceivedPhotoFileName)
            ? null
            : Path.Combine(folder, record.ReceivedPhotoFileName);

        var session = new TradeSession
        {
            SessionRecordId = record.SessionId,
            Note = record.Note,
            ReceivedPhotoPath = receivedPhoto,
            ReceivedValue = record.ReceivedValue,
            CreatedAt = record.CreatedAt,
            ImportedAt = DateTime.UtcNow,
            OutgoingValue = record.OutgoingItems.Sum(i => i.EstimatedValue ?? 0m),
        };
        context.TradeSessions.Add(session);
        context.SaveChanges();

        foreach (var item in record.OutgoingItems)
        {
            context.Trades.Add(new Trade
            {
                TradeSessionId = session.Id,
                TradeRecordId = record.SessionId,
                Game = item.Game,
                CardName = item.CardName,
                SetCode = item.SetCode,
                SetName = item.SetName,
                CollectorNumber = item.CollectorNumber,
                Foil = item.Foil,
                IsOffDatabase = item.IsOffDatabase,
                OffDbPhotoPath = item.IsOffDatabase && !string.IsNullOrEmpty(item.PhotoFileName)
                    ? Path.Combine(folder, item.PhotoFileName)
                    : null,
                EstimatedValue = item.EstimatedValue,
                OriginalLotId = item.IsOffDatabase ? null : item.LotId,
                CreatedAt = record.CreatedAt,
                ImportedAt = DateTime.UtcNow,
            });
        }

        context.SaveChanges();
        logger.LogInformation("Backfilled trade session {SessionId}", record.SessionId);
    }

    // ---- Schema v1: legacy single-card trades ---------------------------------------------------

    private int ProcessLegacyFolder(string text, string jsonPath, string folder)
    {
        TradeRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<TradeRecord>(text, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read legacy trade record at {Path}", jsonPath);
            return 0;
        }

        if (record is null)
            return 0;

        if (record.ProcessedAt is not null)
        {
            // Already applied — but if this record predates the Trade/TradeSession tables, backfill
            // now without reapplying anything else.
            BackfillLegacyIfMissing(record, folder);
            return 0;
        }

        var imported = 0;
        try
        {
            ApplyLegacyTrade(record, folder);
            if (record.ProcessingError is null)
                imported = 1;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to apply trade {TradeId}", record.TradeId);
            record.ProcessingError = ex.Message;
        }

        record.ProcessedAt = DateTime.UtcNow;
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(record, JsonOptions));
        return imported;
    }

    private void ApplyLegacyTrade(TradeRecord record, string folder)
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

        var session = new TradeSession
        {
            SessionRecordId = record.TradeId,
            Note = record.Note,
            ReceivedPhotoPath = photoPath,
            CreatedAt = record.CreatedAt,
            ImportedAt = DateTime.UtcNow,
            OutgoingValue = 0m,
        };
        context.TradeSessions.Add(session);
        context.SaveChanges();

        context.Trades.Add(new Trade
        {
            TradeSessionId = session.Id,
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
        logger.LogInformation("Applied legacy trade {TradeId} to lot {LotId}", record.TradeId, record.LotId);
    }

    /// <summary>Creates the permanent Trade + TradeSession rows for a legacy trade.json that was
    /// already marked processed before those tables existed (or the rows are otherwise missing) — a
    /// no-op once a session exists. Also retrofits any legacy <see cref="InventoryLot.FulfilledTradeId"/>
    /// links onto the new <see cref="InventoryLot.FulfilledTradeSessionId"/>.</summary>
    private void BackfillLegacyIfMissing(TradeRecord record, string folder)
    {
        using var context = dbContextFactory.CreateDbContext();
        if (context.TradeSessions.Any(s => s.SessionRecordId == record.TradeId))
            return;

        var existingTrade = context.Trades.FirstOrDefault(t => t.TradeRecordId == record.TradeId);
        var lot = context.Lots.Include(l => l.Product).FirstOrDefault(l => l.Id == record.LotId);
        var hasSnapshot = !string.IsNullOrEmpty(record.CardName);
        var photoPath = string.IsNullOrEmpty(record.PhotoFileName) ? null : Path.Combine(folder, record.PhotoFileName);

        var session = new TradeSession
        {
            SessionRecordId = record.TradeId,
            Note = record.Note,
            ReceivedPhotoPath = photoPath,
            CreatedAt = record.CreatedAt,
            ImportedAt = DateTime.UtcNow,
            OutgoingValue = 0m,
            FirstFulfilledAt = existingTrade?.FirstFulfilledAt,
        };
        context.TradeSessions.Add(session);
        context.SaveChanges();

        if (existingTrade is not null)
        {
            existingTrade.TradeSessionId = session.Id;
        }
        else
        {
            context.Trades.Add(new Trade
            {
                TradeSessionId = session.Id,
                TradeRecordId = record.TradeId,
                Note = record.Note,
                PhotoPath = photoPath,
                CreatedAt = record.CreatedAt,
                ImportedAt = DateTime.UtcNow,
                OriginalLotId = lot?.Id,
                Game = hasSnapshot ? record.Game : lot?.Product.Game ?? default,
                CardName = hasSnapshot ? record.CardName : lot?.Product.Name ?? "(unknown)",
                SetCode = hasSnapshot ? record.SetCode : lot?.Product.SetCode,
                SetName = hasSnapshot ? record.SetName : lot?.Product.SetName,
                CollectorNumber = hasSnapshot ? record.CollectorNumber : lot?.Product.CollectorNumber,
                Foil = hasSnapshot ? record.Foil : lot?.Product.Foil ?? false,
            });
        }

        // Retrofit legacy fulfillment links onto the session.
        if (existingTrade is not null)
        {
            foreach (var replacement in context.Lots
                         .Where(l => l.FulfilledTradeId == existingTrade.Id && l.FulfilledTradeSessionId == null))
                replacement.FulfilledTradeSessionId = session.Id;
        }

        context.SaveChanges();
        logger.LogInformation("Backfilled TradeSession for pre-existing trade {TradeId}", record.TradeId);
    }
}
