using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using OmniCard.Models;

namespace OmniCard.Data;

/// <summary>
/// Phase 2a, Task 2: ensures the unified <see cref="OmniCardDbContext"/> schema exists on a
/// pre-existing <c>inventory.db</c> (Phase 1 only created Products/Lots/Movements), and performs
/// the one-time migration of <c>collection.db</c> singles data into the unified Product/Lot store.
/// </summary>
public static class UnifiedMigrationService
{
    /// <summary>Marker file (in the data directory) recording that the one-time data migration ran.</summary>
    public const string MigratedFlagFileName = "unified-migration.flag";

    /// <summary>
    /// Persisted old-CollectionCardId -> new-LotId map, written after a successful migration so a
    /// later process (Task 5's FK rename) can remap EbayListing/FlagResolution references without
    /// having to recompute them.
    /// </summary>
    public const string CardToLotMapFileName = "unified-migration-card-to-lot-map.json";

    private const string InventoryDbFileName = "inventory.db";
    private const string CollectionDbFileName = "collection.db";
    private const string InventoryBackupSuffix = ".pre-unified-migration.bak";

    // ---------------------------------------------------------------------
    // Step 1: ensure unified schema exists on a pre-existing inventory.db
    // ---------------------------------------------------------------------

    /// <summary>
    /// Adds any unified-store tables/columns that a Phase-1-only <c>inventory.db</c> is missing.
    /// EF's <c>EnsureCreated()</c> only creates the full schema for a brand-new database file; it is a
    /// no-op against an existing file, so tables added to the model after Phase 1 (StorageContainers,
    /// EbayListings, MismatchLogs, FlagResolutions, ScanDiagnosticEvents) and columns added to existing
    /// tables (Lots.IsMissing/FlagReason, Products.SetName/Color/CardType, Lots.LocationId index) must be
    /// added by hand. Idempotent; no-op if <c>inventory.db</c> does not exist yet (a fresh install will
    /// get the full schema from <c>EnsureCreated()</c> instead).
    /// </summary>
    public static void EnsureUnifiedSchema(string dataDirectory, ILogger logger)
    {
        var dbPath = Path.Combine(dataDirectory, InventoryDbFileName);
        if (!File.Exists(dbPath))
        {
            logger.LogDebug("inventory.db does not exist yet; unified schema will be created fresh by EnsureCreated()");
            return;
        }

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        EnsureUnifiedSchema(conn);
        logger.LogInformation("Unified inventory schema ensured on {Path}", dbPath);
    }

    internal static void EnsureUnifiedSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();

        // New columns on the Phase-1 Products/Lots tables (only if those tables exist at all —
        // a database created fresh by this app's OmniCardDbContext.EnsureCreated() already has them).
        if (TableExists(cmd, "Products"))
        {
            foreach (var col in new[] { "SetName", "Color", "CardType" })
                AddColumnIfMissing(cmd, "Products", col, "TEXT");
        }

        if (TableExists(cmd, "Lots"))
        {
            AddColumnIfMissing(cmd, "Lots", "IsMissing", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(cmd, "Lots", "FlagReason", "TEXT");
            AddColumnIfMissing(cmd, "Lots", "LocationId", "INTEGER");
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Lots_LocationId ON Lots(LocationId)";
            cmd.ExecuteNonQuery();
        }

        // New tables added to OmniCardDbContext after Phase 1.
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS StorageContainers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ContainerType TEXT NOT NULL,
                IsSystem INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                CoverCardId INTEGER,
                ExcludeFromDeckCheck INTEGER NOT NULL DEFAULT 0
            )
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_StorageContainers_Name ON StorageContainers(Name)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS EbayListings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CollectionCardId INTEGER NOT NULL,
                EbayItemId TEXT NOT NULL DEFAULT '',
                EbayCatalogProductId TEXT,
                Status TEXT NOT NULL DEFAULT 'Draft',
                ListingType TEXT NOT NULL DEFAULT 'FixedPrice',
                ListedPrice REAL NOT NULL DEFAULT 0,
                SoldPrice REAL,
                StartTime TEXT,
                EndTime TEXT,
                AuctionDuration INTEGER,
                BuyerUsername TEXT,
                LastSyncedAt TEXT,
                CreatedAt TEXT NOT NULL,
                ErrorMessage TEXT
            )
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_EbayListings_CollectionCardId ON EbayListings(CollectionCardId)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_EbayListings_Status ON EbayListings(Status)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_EbayListings_EbayItemId ON EbayListings(EbayItemId)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS MismatchLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ScanHash INTEGER NOT NULL,
                ScanImagePath TEXT,
                OriginalCardId TEXT NOT NULL DEFAULT '',
                OriginalName TEXT NOT NULL DEFAULT '',
                OriginalSetCode TEXT NOT NULL DEFAULT '',
                OriginalNumber TEXT NOT NULL DEFAULT '',
                OriginalConfidence REAL NOT NULL DEFAULT 0,
                CorrectedCardId TEXT NOT NULL DEFAULT '',
                CorrectedName TEXT NOT NULL DEFAULT '',
                CorrectedSetCode TEXT NOT NULL DEFAULT '',
                CorrectedNumber TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS FlagResolutions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CollectionCardId INTEGER NOT NULL,
                FlagReason TEXT NOT NULL DEFAULT '',
                FixType TEXT NOT NULL DEFAULT '',
                OriginalData TEXT NOT NULL DEFAULT '',
                ResolvedData TEXT NOT NULL DEFAULT '',
                ScanHash INTEGER NOT NULL,
                Confidence REAL,
                FixedAt TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_FlagResolutions_CollectionCardId ON FlagResolutions(CollectionCardId)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ScanDiagnosticEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId TEXT NOT NULL DEFAULT '',
                ScanHash INTEGER NOT NULL,
                EventType TEXT NOT NULL DEFAULT '',
                Timestamp TEXT NOT NULL,
                Payload TEXT NOT NULL DEFAULT ''
            )
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_ScanDiagnosticEvents_ScanHash ON ScanDiagnosticEvents(ScanHash)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_ScanDiagnosticEvents_SessionId ON ScanDiagnosticEvents(SessionId)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_ScanDiagnosticEvents_EventType ON ScanDiagnosticEvents(EventType)";
        cmd.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteCommand cmd, string tableName)
    {
        cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'";
        return (long)cmd.ExecuteScalar()! > 0;
    }

    private static void AddColumnIfMissing(SqliteCommand cmd, string table, string column, string columnDefinition)
    {
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
        if ((long)cmd.ExecuteScalar()! == 0)
        {
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnDefinition}";
            cmd.ExecuteNonQuery();
        }
    }

    // ---------------------------------------------------------------------
    // Step 2: one-time data migration collection.db -> unified store
    // ---------------------------------------------------------------------

    /// <summary>
    /// Migrates all <c>collection.db</c> data (Cards, StorageContainers, MismatchLogs,
    /// FlagResolutions, ScanDiagnosticEvents, EbayListings) into the unified
    /// <see cref="OmniCardDbContext"/> store. Guarded by a persisted flag file so it only runs once.
    /// Returns the old-CollectionCardId -> new-LotId map (empty if nothing was migrated), which is
    /// also persisted to <see cref="CardToLotMapFileName"/> for Task 5 to consume later.
    /// </summary>
    public static IReadOnlyDictionary<int, int> MigrateDataIfNeeded(
        string dataDirectory,
        IDbContextFactory<CollectionDbContext> collectionFactory,
        IDbContextFactory<OmniCardDbContext> unifiedFactory,
        ILogger logger)
    {
        var flagPath = Path.Combine(dataDirectory, MigratedFlagFileName);
        if (File.Exists(flagPath))
        {
            logger.LogDebug("Unified data migration already completed; skipping");
            return new Dictionary<int, int>();
        }

        var collectionDbPath = Path.Combine(dataDirectory, CollectionDbFileName);
        if (!File.Exists(collectionDbPath))
        {
            logger.LogDebug("No collection.db found; nothing to migrate into the unified store");
            WriteFlag(flagPath);
            return new Dictionary<int, int>();
        }

        logger.LogInformation("Starting one-time migration of collection.db into the unified Product/Lot store");

        BackupInventoryDb(dataDirectory, logger);

        using var collectionCtx = collectionFactory.CreateDbContext();
        using var unifiedCtx = unifiedFactory.CreateDbContext();

        using var transaction = unifiedCtx.Database.BeginTransaction();
        try
        {
            // --- Containers first (Lots.LocationId needs the remapped container ids) ---
            var oldContainers = collectionCtx.StorageContainers.AsNoTracking().ToList();
            var newContainers = oldContainers.Select(c => new StorageContainer
            {
                Name = c.Name,
                ContainerType = c.ContainerType,
                IsSystem = c.IsSystem,
                SortOrder = c.SortOrder,
                CoverCardId = c.CoverCardId, // remapped to LotId below, once the card map is known
                ExcludeFromDeckCheck = c.ExcludeFromDeckCheck,
            }).ToList();
            unifiedCtx.StorageContainers.AddRange(newContainers);
            unifiedCtx.SaveChanges();

            var containerIdMap = new Dictionary<int, int>();
            for (int i = 0; i < oldContainers.Count; i++)
                containerIdMap[oldContainers[i].Id] = newContainers[i].Id;

            // --- Products (deduped) + Lots (one per card) ---
            var cards = collectionCtx.Cards.AsNoTracking().ToList();
            var productMap = new Dictionary<(CardGame Game, string GameCardId, bool Foil), Product>();
            foreach (var card in cards)
            {
                var key = (card.Game, card.GameCardId, card.IsFoil);
                if (productMap.ContainsKey(key))
                    continue;

                var product = new Product
                {
                    Game = card.Game,
                    Category = ProductCategory.Single,
                    Name = card.Name,
                    SetName = card.SetName,
                    SetCode = card.SetCode,
                    GameCardId = card.GameCardId,
                    CollectorNumber = card.Number,
                    Rarity = card.Rarity,
                    Foil = card.IsFoil,
                    Color = card.Color,
                    CardType = card.CardType,
                    ImageUri = card.ImageUri,
                };
                productMap[key] = product;
            }
            unifiedCtx.Products.AddRange(productMap.Values);
            unifiedCtx.SaveChanges();

            var lotsByCard = new List<(CollectionCard Card, InventoryLot Lot)>();
            foreach (var card in cards)
            {
                var product = productMap[(card.Game, card.GameCardId, card.IsFoil)];
                var lot = new InventoryLot
                {
                    ProductId = product.Id,
                    Quantity = 1,
                    UnitCost = card.PurchasePrice,
                    AcquisitionDate = card.DateAdded,
                    LocationId = card.ContainerId.HasValue && containerIdMap.TryGetValue(card.ContainerId.Value, out var newLoc)
                        ? newLoc
                        : null,
                    Condition = card.Condition,
                    ScanImagePath = card.ScanImagePath,
                    Page = card.Page,
                    Slot = card.Slot,
                    Section = card.Section,
                    IsMissing = card.IsMissing,
                    FlagReason = card.FlagReason,
                };
                lotsByCard.Add((card, lot));
            }
            unifiedCtx.Lots.AddRange(lotsByCard.Select(x => x.Lot));
            unifiedCtx.SaveChanges();

            var cardIdToLotId = new Dictionary<int, int>();
            foreach (var (card, lot) in lotsByCard)
                cardIdToLotId[card.Id] = lot.Id;

            // Seed one Acquire movement per lot.
            var movements = lotsByCard.Select(x => new InventoryMovement
            {
                ProductId = x.Lot.ProductId,
                LotId = x.Lot.Id,
                Type = MovementType.Acquire,
                Quantity = 1,
                UnitValue = x.Lot.UnitCost,
                Timestamp = x.Lot.AcquisitionDate,
            });
            unifiedCtx.Movements.AddRange(movements);

            // Remap StorageContainer.CoverCardId (old CollectionCardId) -> new LotId, now that the
            // card map exists. Best effort: leave the old value if the referenced card wasn't found.
            foreach (var container in newContainers)
            {
                if (container.CoverCardId is int oldCoverCardId && cardIdToLotId.TryGetValue(oldCoverCardId, out var newLotId))
                    container.CoverCardId = newLotId;
            }

            // --- MismatchLogs (no card FK; straight copy) ---
            var mismatchLogs = collectionCtx.MismatchLogs.AsNoTracking().Select(m => new MismatchLog
            {
                ScanHash = m.ScanHash,
                ScanImagePath = m.ScanImagePath,
                OriginalCardId = m.OriginalCardId,
                OriginalName = m.OriginalName,
                OriginalSetCode = m.OriginalSetCode,
                OriginalNumber = m.OriginalNumber,
                OriginalConfidence = m.OriginalConfidence,
                CorrectedCardId = m.CorrectedCardId,
                CorrectedName = m.CorrectedName,
                CorrectedSetCode = m.CorrectedSetCode,
                CorrectedNumber = m.CorrectedNumber,
                CreatedAt = m.CreatedAt,
            }).ToList();
            unifiedCtx.MismatchLogs.AddRange(mismatchLogs);

            // --- FlagResolutions (CollectionCardId remapped to the new LotId) ---
            var flagResolutions = collectionCtx.FlagResolutions.AsNoTracking().ToList().Select(f => new FlagResolution
            {
                CollectionCardId = cardIdToLotId.TryGetValue(f.CollectionCardId, out var lotId) ? lotId : f.CollectionCardId,
                FlagReason = f.FlagReason,
                FixType = f.FixType,
                OriginalData = f.OriginalData,
                ResolvedData = f.ResolvedData,
                ScanHash = f.ScanHash,
                Confidence = f.Confidence,
                FixedAt = f.FixedAt,
                CreatedAt = f.CreatedAt,
            }).ToList();
            unifiedCtx.FlagResolutions.AddRange(flagResolutions);

            // --- ScanDiagnosticEvents (no card FK; straight copy) ---
            var diagnosticEvents = collectionCtx.ScanDiagnosticEvents.AsNoTracking().Select(d => new ScanDiagnosticEvent
            {
                SessionId = d.SessionId,
                ScanHash = d.ScanHash,
                EventType = d.EventType,
                Timestamp = d.Timestamp,
                Payload = d.Payload,
            }).ToList();
            unifiedCtx.ScanDiagnosticEvents.AddRange(diagnosticEvents);

            // --- EbayListings (CollectionCardId remapped to the new LotId) ---
            var ebayListings = collectionCtx.EbayListings.AsNoTracking().ToList().Select(l => new EbayListing
            {
                CollectionCardId = cardIdToLotId.TryGetValue(l.CollectionCardId, out var lotId) ? lotId : l.CollectionCardId,
                EbayItemId = l.EbayItemId,
                EbayCatalogProductId = l.EbayCatalogProductId,
                Status = l.Status,
                ListingType = l.ListingType,
                ListedPrice = l.ListedPrice,
                SoldPrice = l.SoldPrice,
                StartTime = l.StartTime,
                EndTime = l.EndTime,
                AuctionDuration = l.AuctionDuration,
                BuyerUsername = l.BuyerUsername,
                LastSyncedAt = l.LastSyncedAt,
                CreatedAt = l.CreatedAt,
                ErrorMessage = l.ErrorMessage,
            }).ToList();
            unifiedCtx.EbayListings.AddRange(ebayListings);

            unifiedCtx.SaveChanges();

            transaction.Commit();

            logger.LogInformation(
                "Unified migration complete: {Products} products, {Lots} lots, {Containers} containers, " +
                "{Mismatches} mismatch logs, {Flags} flag resolutions, {Diagnostics} diagnostic events, {Ebay} eBay listings",
                productMap.Count, lotsByCard.Count, newContainers.Count,
                mismatchLogs.Count, flagResolutions.Count, diagnosticEvents.Count, ebayListings.Count);

            PersistCardToLotMap(dataDirectory, cardIdToLotId, logger);
            WriteFlag(flagPath);

            return cardIdToLotId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void BackupInventoryDb(string dataDirectory, ILogger logger)
    {
        var inventoryDbPath = Path.Combine(dataDirectory, InventoryDbFileName);
        if (!File.Exists(inventoryDbPath))
            return;

        SqliteConnection.ClearAllPools();
        var backupPath = Path.Combine(dataDirectory, InventoryDbFileName + InventoryBackupSuffix);
        File.Copy(inventoryDbPath, backupPath, overwrite: true);
        logger.LogInformation(
            "Backed up inventory.db to {BackupPath} before unified migration (collection.db is left untouched on disk for rollback)",
            backupPath);
    }

    private static void PersistCardToLotMap(string dataDirectory, Dictionary<int, int> map, ILogger logger)
    {
        try
        {
            var path = Path.Combine(dataDirectory, CardToLotMapFileName);
            File.WriteAllText(path, JsonSerializer.Serialize(map));
            logger.LogInformation("Persisted CollectionCardId->LotId map ({Count} entries) to {Path}", map.Count, path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist CollectionCardId->LotId map");
        }
    }

    private static void WriteFlag(string flagPath)
    {
        File.WriteAllText(flagPath, DateTime.UtcNow.ToString("O"));
    }
}
