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

    /// <summary>
    /// Key of the <see cref="Models.MigrationState"/> row inserted (atomically, in the same
    /// transaction as the migrated data) once the one-time migration completes. This DB marker —
    /// not the file flag below — is authoritative for "has this migration already run?", because
    /// it commits (or rolls back) together with the data it describes.
    /// </summary>
    public const string MigrationStateKey = "UnifiedDataMigration";

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

        // If EbayListings already exists from before the Task-5 rename, get its FK column
        // renamed to LotId before (re)running the CREATE TABLE/index statements below — SQLite's
        // CREATE TABLE IF NOT EXISTS is a no-op on an existing table, so the column must already
        // be named right by the time we get there. This does NOT retrofit a database-level FK
        // constraint onto that pre-existing table (SQLite can't ALTER TABLE to add one); only a
        // fresh CREATE TABLE (below) gets the real FK/cascade. See Task 5 report for detail.
        if (TableExists(cmd, "EbayListings"))
            RenameColumnIfPresent(cmd, "EbayListings", "CollectionCardId", "LotId");

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS EbayListings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LotId INTEGER NOT NULL,
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
                ErrorMessage TEXT,
                FOREIGN KEY (LotId) REFERENCES Lots(Id) ON DELETE CASCADE
            )
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText = "DROP INDEX IF EXISTS IX_EbayListings_CollectionCardId";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_EbayListings_LotId ON EbayListings(LotId)";
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

        // Same rename-in-place guard as EbayListings above.
        if (TableExists(cmd, "FlagResolutions"))
            RenameColumnIfPresent(cmd, "FlagResolutions", "CollectionCardId", "LotId");

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS FlagResolutions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LotId INTEGER NOT NULL,
                FlagReason TEXT NOT NULL DEFAULT '',
                FixType TEXT NOT NULL DEFAULT '',
                OriginalData TEXT NOT NULL DEFAULT '',
                ResolvedData TEXT NOT NULL DEFAULT '',
                ScanHash INTEGER NOT NULL,
                Confidence REAL,
                FixedAt TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (LotId) REFERENCES Lots(Id) ON DELETE CASCADE
            )
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText = "DROP INDEX IF EXISTS IX_FlagResolutions_CollectionCardId";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_FlagResolutions_LotId ON FlagResolutions(LotId)";
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

        // Marker table used to atomically record one-time data migrations alongside the data
        // they migrate (see MigrateDataIfNeeded's use of the MigrationState DbSet).
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS MigrationState (
                Key TEXT NOT NULL PRIMARY KEY,
                CompletedAt TEXT NOT NULL
            )
            """;
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

    private static bool ColumnExists(SqliteCommand cmd, string table, string column)
    {
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
        return (long)cmd.ExecuteScalar()! > 0;
    }

    /// <summary>
    /// Renames <paramref name="oldColumn"/> to <paramref name="newColumn"/> on <paramref name="table"/>
    /// if the old name is still present and the new one isn't yet — idempotent, and a no-op for a
    /// brand-new table created with the new name in the first place. Used to carry Task-5's
    /// CollectionCardId -> LotId rename onto an <c>inventory.db</c> that already had these tables
    /// under their pre-rename column name. Requires SQLite 3.25+ (bundled by Microsoft.Data.Sqlite),
    /// which supports ALTER TABLE ... RENAME COLUMN.
    /// </summary>
    private static void RenameColumnIfPresent(SqliteCommand cmd, string table, string oldColumn, string newColumn)
    {
        if (ColumnExists(cmd, table, newColumn) || !ColumnExists(cmd, table, oldColumn))
            return;

        cmd.CommandText = $"ALTER TABLE {table} RENAME COLUMN {oldColumn} TO {newColumn}";
        cmd.ExecuteNonQuery();
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

        // The DB marker is authoritative: it is committed atomically with the migrated data (or
        // rolled back with it), so it can't disagree with what's actually in the unified store the
        // way a separately-written file flag can after a crash between the data commit and the
        // flag write. The file flag is still written below for cheap external inspection, but it is
        // never consulted here.
        using (var checkCtx = unifiedFactory.CreateDbContext())
        {
            if (checkCtx.MigrationState.AsNoTracking().Any(m => m.Key == MigrationStateKey))
            {
                logger.LogDebug("Unified data migration already completed (DB marker present); skipping");
                return new Dictionary<int, int>();
            }
        }

        var collectionDbPath = Path.Combine(dataDirectory, CollectionDbFileName);
        if (!File.Exists(collectionDbPath))
        {
            logger.LogDebug("No collection.db found; nothing to migrate into the unified store");
            MarkMigrationComplete(unifiedFactory);
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

            // Seed one Acquire movement per lot. The Acquire movement's Timestamp records "when
            // this entry was recorded" and is intentionally independent of the (possibly backdated)
            // Lot.AcquisitionDate — see InventoryService.AddLot's identical convention.
            var recordedAt = DateTime.UtcNow;
            var movements = lotsByCard.Select(x => new InventoryMovement
            {
                ProductId = x.Lot.ProductId,
                LotId = x.Lot.Id,
                Type = MovementType.Acquire,
                Quantity = 1,
                UnitValue = x.Lot.UnitCost,
                Timestamp = recordedAt,
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

            // --- FlagResolutions (old CollectionCardId remapped to the new LotId) ---
            var flagResolutions = collectionCtx.FlagResolutions.AsNoTracking().ToList().Select(f => new FlagResolution
            {
                LotId = cardIdToLotId.TryGetValue(f.LotId, out var lotId) ? lotId : f.LotId,
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

            // --- EbayListings (old CollectionCardId remapped to the new LotId) ---
            var ebayListings = collectionCtx.EbayListings.AsNoTracking().ToList().Select(l => new EbayListing
            {
                LotId = cardIdToLotId.TryGetValue(l.LotId, out var lotId) ? lotId : l.LotId,
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

            // Insert the "migration complete" marker in the SAME transaction as the data above, so
            // it is atomic with it: a crash before Commit() leaves neither the data nor the marker
            // committed, and a re-run starts clean instead of duplicating Products/Lots/Movements.
            unifiedCtx.MigrationState.Add(new MigrationState { Key = MigrationStateKey, CompletedAt = recordedAt });

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

        var backupPath = Path.Combine(dataDirectory, InventoryDbFileName + InventoryBackupSuffix);
        if (File.Exists(backupPath))
        {
            // Never overwrite an existing backup: it is the clean pre-migration snapshot taken on
            // the FIRST attempt. If a retry (e.g. after a crash) overwrote it with an in-progress or
            // already-migrated inventory.db, the original rollback point would be lost.
            logger.LogInformation(
                "Pre-migration backup already exists at {BackupPath}; leaving it untouched", backupPath);
            return;
        }

        SqliteConnection.ClearAllPools();
        File.Copy(inventoryDbPath, backupPath, overwrite: false);
        logger.LogInformation(
            "Backed up inventory.db to {BackupPath} before unified migration (collection.db is left untouched on disk for rollback)",
            backupPath);
    }

    /// <summary>
    /// Writes the DB migration marker for the "nothing to migrate" (no collection.db) path, where
    /// there is no other data to make it atomic with — a plain SaveChanges is sufficient.
    /// </summary>
    private static void MarkMigrationComplete(IDbContextFactory<OmniCardDbContext> unifiedFactory)
    {
        using var ctx = unifiedFactory.CreateDbContext();
        if (!ctx.MigrationState.Any(m => m.Key == MigrationStateKey))
        {
            ctx.MigrationState.Add(new MigrationState { Key = MigrationStateKey, CompletedAt = DateTime.UtcNow });
            ctx.SaveChanges();
        }
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
