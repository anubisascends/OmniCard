using Microsoft.Data.Sqlite;
using OmniCard.Data;

namespace OmniCard.Tests.Services;

public class UnifiedMigrationTests : IDisposable
{
    private readonly string _tempDir;

    public UnifiedMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardUnifiedMigrationTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void EnsureUnifiedSchema_AddsMissingTablesAndColumns_ToPhase1OnlyInventoryDb()
    {
        var dbPath = Path.Combine(_tempDir, "phase1-inventory.db");
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            // Minimal Phase-1 schema: Products/Lots without the columns/tables added since.
            cmd.CommandText = """
                CREATE TABLE Products (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Game TEXT NOT NULL,
                    Category TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    SetCode TEXT,
                    Upc TEXT,
                    GameCardId TEXT,
                    CollectorNumber TEXT,
                    Rarity TEXT,
                    Foil INTEGER NOT NULL DEFAULT 0,
                    ImageUri TEXT
                );
                CREATE TABLE Lots (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProductId INTEGER NOT NULL,
                    Quantity INTEGER NOT NULL DEFAULT 1,
                    UnitCost REAL,
                    AcquisitionDate TEXT NOT NULL,
                    Source TEXT,
                    LocationId INTEGER,
                    Condition TEXT,
                    ScanImagePath TEXT,
                    Page INTEGER,
                    Slot INTEGER,
                    Section TEXT
                );
                CREATE TABLE Movements (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProductId INTEGER NOT NULL,
                    LotId INTEGER,
                    Type TEXT NOT NULL,
                    Quantity INTEGER NOT NULL,
                    UnitValue REAL,
                    Timestamp TEXT NOT NULL,
                    Note TEXT,
                    RelatedMovementId INTEGER
                );
                """;
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        // Sanity: a directory with no inventory.db at all is a no-op (doesn't throw).
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);
        UnifiedMigrationService.EnsureUnifiedSchema(emptyDir, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        // Real call: copy the Phase-1 db to the conventional "inventory.db" filename.
        var realDir = Path.Combine(_tempDir, "real");
        Directory.CreateDirectory(realDir);
        File.Copy(dbPath, Path.Combine(realDir, "inventory.db"));

        UnifiedMigrationService.EnsureUnifiedSchema(realDir, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        using var verifyConn = new SqliteConnection($"Data Source={Path.Combine(realDir, "inventory.db")}");
        verifyConn.Open();
        using var verifyCmd = verifyConn.CreateCommand();

        foreach (var table in new[] { "StorageContainers", "EbayListings", "MismatchLogs", "FlagResolutions", "ScanDiagnosticEvents", "MigrationState" })
        {
            verifyCmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'";
            Assert.Equal(1L, (long)verifyCmd.ExecuteScalar()!);
        }

        foreach (var (table, column) in new[] { ("Products", "SetName"), ("Products", "Color"), ("Products", "CardType"), ("Lots", "IsMissing"), ("Lots", "FlagReason") })
        {
            verifyCmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
            Assert.True((long)verifyCmd.ExecuteScalar()! > 0, $"{table}.{column} should exist");
        }

        // Idempotent — running again on the already-patched db must not throw.
        SqliteConnection.ClearAllPools();
        UnifiedMigrationService.EnsureUnifiedSchema(realDir, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        verifyConn.Dispose();
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void EnsureUnifiedSchema_RenamesLegacyCollectionCardIdColumn_ToLotId()
    {
        // Simulates an inventory.db from earlier in Task 5's own development, before the
        // CollectionCardId -> LotId rename: EbayListings/FlagResolutions already exist under
        // their pre-rename column name.
        var dir = Path.Combine(_tempDir, "legacy-column-name");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "inventory.db");

        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE EbayListings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CollectionCardId INTEGER NOT NULL,
                    EbayItemId TEXT NOT NULL DEFAULT '',
                    Status TEXT NOT NULL DEFAULT 'Draft',
                    ListingType TEXT NOT NULL DEFAULT 'FixedPrice',
                    ListedPrice REAL NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IX_EbayListings_CollectionCardId ON EbayListings(CollectionCardId);
                CREATE TABLE FlagResolutions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CollectionCardId INTEGER NOT NULL,
                    FlagReason TEXT NOT NULL DEFAULT '',
                    FixType TEXT NOT NULL DEFAULT '',
                    OriginalData TEXT NOT NULL DEFAULT '',
                    ResolvedData TEXT NOT NULL DEFAULT '',
                    ScanHash INTEGER NOT NULL,
                    FixedAt TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                CREATE INDEX IX_FlagResolutions_CollectionCardId ON FlagResolutions(CollectionCardId);
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO EbayListings (CollectionCardId, EbayItemId, CreatedAt) VALUES (7, 'item-7', '2026-01-01')";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO FlagResolutions (CollectionCardId, ScanHash, FixedAt, CreatedAt) VALUES (7, 42, '2026-01-01', '2026-01-01')";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        UnifiedMigrationService.EnsureUnifiedSchema(dir, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        using var verifyConn = new SqliteConnection($"Data Source={dbPath}");
        verifyConn.Open();
        using var verifyCmd = verifyConn.CreateCommand();

        foreach (var table in new[] { "EbayListings", "FlagResolutions" })
        {
            verifyCmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = 'LotId'";
            Assert.Equal(1L, (long)verifyCmd.ExecuteScalar()!);
            verifyCmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = 'CollectionCardId'";
            Assert.Equal(0L, (long)verifyCmd.ExecuteScalar()!);
        }

        // Existing row data survives the rename under the new column name.
        verifyCmd.CommandText = "SELECT LotId FROM EbayListings WHERE EbayItemId = 'item-7'";
        Assert.Equal(7L, (long)verifyCmd.ExecuteScalar()!);
        verifyCmd.CommandText = "SELECT LotId FROM FlagResolutions WHERE ScanHash = 42";
        Assert.Equal(7L, (long)verifyCmd.ExecuteScalar()!);

        // Idempotent — running again must not throw or re-attempt the rename.
        verifyConn.Dispose();
        SqliteConnection.ClearAllPools();
        UnifiedMigrationService.EnsureUnifiedSchema(dir, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        SqliteConnection.ClearAllPools();
    }
}
