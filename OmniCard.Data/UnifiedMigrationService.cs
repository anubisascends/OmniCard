using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.IO;

namespace OmniCard.Data;

/// <summary>
/// Phase 2a, Task 2: ensures the unified <see cref="OmniCardDbContext"/> schema exists on a
/// pre-existing <c>inventory.db</c> (Phase 1 only created Products/Lots/Movements).
/// </summary>
/// <remarks>
/// This service previously (Task 3) also performed the one-time migration of <c>collection.db</c>
/// singles data into the unified Product/Lot store — see <c>MigrateDataIfNeeded</c> in git history.
/// As of Task 6, <c>CollectionDbContext</c> and the old <c>collection.db</c>-reading migration path
/// are retired: the migration already ran (recorded by a <see cref="Models.MigrationState"/> row
/// with key <c>"UnifiedDataMigration"</c>), and the app no longer opens <c>collection.db</c> at all.
/// The file is left on disk for rollback but is otherwise inert.
/// </remarks>
public static class UnifiedMigrationService
{
    private const string InventoryDbFileName = "inventory.db";

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

}
