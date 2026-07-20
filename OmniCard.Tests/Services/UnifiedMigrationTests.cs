using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class UnifiedMigrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteConnection _unifiedConnection;
    private readonly DbContextOptions<OmniCardDbContext> _unifiedOptions;
    private readonly UnifiedDbContextFactory _unifiedFactory;
    private readonly DateTime _testStart = DateTime.UtcNow;

    public UnifiedMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardUnifiedMigrationTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _unifiedConnection = new SqliteConnection("Data Source=:memory:");
        _unifiedConnection.Open();
        _unifiedOptions = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_unifiedConnection).Options;
        using (var ctx = new OmniCardDbContext(_unifiedOptions))
            ctx.Database.EnsureCreated();
        _unifiedFactory = new UnifiedDbContextFactory(_unifiedOptions);
    }

    public void Dispose()
    {
        _unifiedConnection.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private OmniCardDbContext NewUnifiedCtx() => new(_unifiedOptions);

    /// <summary>
    /// Creates a raw legacy collection.db (Cards/StorageContainers/EbayListings/FlagResolutions/
    /// MismatchLogs/ScanDiagnosticEvents) file at the conventional path inside <see cref="_tempDir"/>,
    /// matching the schema the pre-Task-6 <c>CollectionDbContext</c> used to produce (EbayListings
    /// and FlagResolutions still physically keyed on <c>CollectionCardId</c>).
    /// </summary>
    private string CreateLegacyCollectionDb()
    {
        var path = Path.Combine(_tempDir, "collection.db");
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Cards (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Game TEXT NOT NULL,
                GameCardId TEXT NOT NULL,
                Name TEXT NOT NULL,
                SetName TEXT NOT NULL DEFAULT '',
                SetCode TEXT NOT NULL DEFAULT '',
                Number TEXT NOT NULL DEFAULT '',
                Rarity TEXT NOT NULL DEFAULT '',
                ImageUri TEXT,
                ScanImagePath TEXT,
                Condition TEXT NOT NULL DEFAULT 'NM',
                IsFoil INTEGER NOT NULL DEFAULT 0,
                PurchasePrice TEXT,
                DateAdded TEXT NOT NULL,
                ContainerId INTEGER,
                Page INTEGER,
                Slot INTEGER,
                Section TEXT,
                Color TEXT,
                CardType TEXT,
                IsMissing INTEGER NOT NULL DEFAULT 0,
                FlagReason TEXT
            );
            CREATE TABLE StorageContainers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ContainerType TEXT NOT NULL,
                IsSystem INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                CoverCardId INTEGER,
                ExcludeFromDeckCheck INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE EbayListings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CollectionCardId INTEGER NOT NULL,
                EbayItemId TEXT NOT NULL DEFAULT '',
                EbayCatalogProductId TEXT,
                Status TEXT NOT NULL DEFAULT 'Draft',
                ListingType TEXT NOT NULL DEFAULT 'FixedPrice',
                ListedPrice TEXT NOT NULL DEFAULT '0',
                SoldPrice TEXT,
                StartTime TEXT,
                EndTime TEXT,
                AuctionDuration INTEGER,
                BuyerUsername TEXT,
                LastSyncedAt TEXT,
                CreatedAt TEXT NOT NULL,
                ErrorMessage TEXT
            );
            CREATE TABLE FlagResolutions (
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
            );
            CREATE TABLE MismatchLogs (
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
            );
            CREATE TABLE ScanDiagnosticEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId TEXT NOT NULL DEFAULT '',
                ScanHash INTEGER NOT NULL,
                EventType TEXT NOT NULL DEFAULT '',
                Timestamp TEXT NOT NULL,
                Payload TEXT NOT NULL DEFAULT ''
            );
            """;
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
        return path;
    }

    private static int InsertCard(
        SqliteConnection conn, CardGame game, string gameCardId, string name, bool isFoil,
        string setName = "", string setCode = "", string number = "", string rarity = "",
        string? imageUri = null, string? scanImagePath = null, string condition = "NM",
        decimal? purchasePrice = null, DateTime? dateAdded = null, int? containerId = null,
        int? page = null, int? slot = null, string? section = null, string? color = null,
        string? cardType = null, bool isMissing = false, string? flagReason = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Cards
                (Game, GameCardId, Name, SetName, SetCode, Number, Rarity, ImageUri, ScanImagePath,
                 Condition, IsFoil, PurchasePrice, DateAdded, ContainerId, Page, Slot, Section, Color,
                 CardType, IsMissing, FlagReason)
            VALUES
                ($game, $gameCardId, $name, $setName, $setCode, $number, $rarity, $imageUri, $scanImagePath,
                 $condition, $isFoil, $purchasePrice, $dateAdded, $containerId, $page, $slot, $section, $color,
                 $cardType, $isMissing, $flagReason);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$game", game.ToString());
        cmd.Parameters.AddWithValue("$gameCardId", gameCardId);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$setName", setName);
        cmd.Parameters.AddWithValue("$setCode", setCode);
        cmd.Parameters.AddWithValue("$number", number);
        cmd.Parameters.AddWithValue("$rarity", rarity);
        cmd.Parameters.AddWithValue("$imageUri", imageUri is null ? DBNull.Value : imageUri);
        cmd.Parameters.AddWithValue("$scanImagePath", scanImagePath is null ? DBNull.Value : scanImagePath);
        cmd.Parameters.AddWithValue("$condition", condition);
        cmd.Parameters.AddWithValue("$isFoil", isFoil ? 1 : 0);
        cmd.Parameters.AddWithValue("$purchasePrice", purchasePrice is null ? DBNull.Value : purchasePrice.Value.ToString());
        cmd.Parameters.AddWithValue("$dateAdded", (dateAdded ?? DateTime.UtcNow).ToString("O"));
        cmd.Parameters.AddWithValue("$containerId", containerId is null ? DBNull.Value : containerId.Value);
        cmd.Parameters.AddWithValue("$page", page is null ? DBNull.Value : page.Value);
        cmd.Parameters.AddWithValue("$slot", slot is null ? DBNull.Value : slot.Value);
        cmd.Parameters.AddWithValue("$section", section is null ? DBNull.Value : section);
        cmd.Parameters.AddWithValue("$color", color is null ? DBNull.Value : color);
        cmd.Parameters.AddWithValue("$cardType", cardType is null ? DBNull.Value : cardType);
        cmd.Parameters.AddWithValue("$isMissing", isMissing ? 1 : 0);
        cmd.Parameters.AddWithValue("$flagReason", flagReason is null ? DBNull.Value : flagReason);
        return (int)(long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Creates a legacy collection.db whose <c>Cards</c> table is missing every column added after
    /// v1 (ScanImagePath, Color, CardType, IsMissing, FlagReason) — simulating a <c>collection.db</c>
    /// carried forward from a release that predates the (now-deleted) migrations that added them.
    /// Used by <see cref="MigrateDataIfNeeded_LegacyCardsTableMissingLateAddedColumns_MigratesWithoutThrowing"/>
    /// to verify the reader tolerates this instead of throwing "no such column".
    /// </summary>
    private string CreateLegacyCollectionDbMissingOptionalColumns()
    {
        var path = Path.Combine(_tempDir, "collection.db");
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Cards (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Game TEXT NOT NULL,
                GameCardId TEXT NOT NULL,
                Name TEXT NOT NULL,
                SetName TEXT NOT NULL DEFAULT '',
                SetCode TEXT NOT NULL DEFAULT '',
                Number TEXT NOT NULL DEFAULT '',
                Rarity TEXT NOT NULL DEFAULT '',
                ImageUri TEXT,
                Condition TEXT NOT NULL DEFAULT 'NM',
                IsFoil INTEGER NOT NULL DEFAULT 0,
                PurchasePrice TEXT,
                DateAdded TEXT NOT NULL,
                ContainerId INTEGER,
                Page INTEGER,
                Slot INTEGER,
                Section TEXT
            );
            """;
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
        return path;
    }

    private static int InsertCardMissingOptionalColumns(
        SqliteConnection conn, CardGame game, string gameCardId, string name, bool isFoil,
        string setName = "", string setCode = "", string number = "", string rarity = "",
        decimal? purchasePrice = null, DateTime? dateAdded = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Cards (Game, GameCardId, Name, SetName, SetCode, Number, Rarity, IsFoil, PurchasePrice, DateAdded)
            VALUES ($game, $gameCardId, $name, $setName, $setCode, $number, $rarity, $isFoil, $purchasePrice, $dateAdded);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$game", game.ToString());
        cmd.Parameters.AddWithValue("$gameCardId", gameCardId);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$setName", setName);
        cmd.Parameters.AddWithValue("$setCode", setCode);
        cmd.Parameters.AddWithValue("$number", number);
        cmd.Parameters.AddWithValue("$rarity", rarity);
        cmd.Parameters.AddWithValue("$isFoil", isFoil ? 1 : 0);
        cmd.Parameters.AddWithValue("$purchasePrice", purchasePrice is null ? DBNull.Value : purchasePrice.Value.ToString());
        cmd.Parameters.AddWithValue("$dateAdded", (dateAdded ?? DateTime.UtcNow).ToString("O"));
        return (int)(long)cmd.ExecuteScalar()!;
    }

    private static int InsertContainer(SqliteConnection conn, string name, ContainerType type, int? coverCardId = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO StorageContainers (Name, ContainerType, SortOrder, CoverCardId)
            VALUES ($name, $type, 1, $coverCardId);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$type", type.ToString());
        cmd.Parameters.AddWithValue("$coverCardId", coverCardId is null ? DBNull.Value : coverCardId.Value);
        return (int)(long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void MigrateDataIfNeeded_DedupsProductsByGameCardIdFoil_AndMapsLotFields()
    {
        CreateLegacyCollectionDb();
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(_tempDir, "collection.db")}"))
        {
            conn.Open();
            InsertCard(conn, CardGame.Mtg, "bolt-1", "Lightning Bolt", isFoil: false,
                setName: "Alpha", setCode: "lea", number: "1", rarity: "common", color: "R", cardType: "Instant",
                imageUri: "img/bolt.jpg", condition: "NM", purchasePrice: 5.50m,
                dateAdded: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                scanImagePath: "scans/bolt1.jpg", page: 2, slot: 3, section: "A",
                isMissing: false, flagReason: "None");

            // Second copy of the same card/foil -> same Product, separate Lot.
            InsertCard(conn, CardGame.Mtg, "bolt-1", "Lightning Bolt", isFoil: false,
                setName: "Alpha", setCode: "lea", number: "1", rarity: "common", condition: "LP",
                purchasePrice: 4.00m, dateAdded: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

            // Foil variant of the same card -> distinct Product.
            InsertCard(conn, CardGame.Mtg, "bolt-1", "Lightning Bolt", isFoil: true,
                setName: "Alpha", setCode: "lea", number: "1", rarity: "common", condition: "NM",
                purchasePrice: 12.00m, dateAdded: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                isMissing: true, flagReason: "Manual");
        }
        SqliteConnection.ClearAllPools();

        UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _unifiedFactory, NullLogger.Instance);

        using var verify = NewUnifiedCtx();
        var products = verify.Products.AsNoTracking().ToList();
        Assert.Equal(2, products.Count); // deduped by (Game, GameCardId, Foil)

        var nonFoil = Assert.Single(products, p => !p.Foil);
        Assert.Equal(ProductCategory.Single, nonFoil.Category);
        Assert.Equal("Lightning Bolt", nonFoil.Name);
        Assert.Equal("Alpha", nonFoil.SetName);
        Assert.Equal("lea", nonFoil.SetCode);
        Assert.Equal("1", nonFoil.CollectorNumber);
        Assert.Equal("common", nonFoil.Rarity);
        Assert.Equal("R", nonFoil.Color);
        Assert.Equal("Instant", nonFoil.CardType);
        Assert.Equal("img/bolt.jpg", nonFoil.ImageUri);

        var foil = Assert.Single(products, p => p.Foil);
        Assert.NotEqual(nonFoil.Id, foil.Id);

        var lots = verify.Lots.AsNoTracking().ToList();
        Assert.Equal(3, lots.Count); // one lot per card row

        var nmLot = Assert.Single(lots, l => l.Condition == "NM" && l.ProductId == nonFoil.Id);
        Assert.Equal(1, nmLot.Quantity);
        Assert.Equal(5.50m, nmLot.UnitCost);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), nmLot.AcquisitionDate);
        Assert.Equal("scans/bolt1.jpg", nmLot.ScanImagePath);
        Assert.Equal(2, nmLot.Page);
        Assert.Equal(3, nmLot.Slot);
        Assert.Equal("A", nmLot.Section);
        Assert.False(nmLot.IsMissing);
        Assert.Equal(FlagReason.None, nmLot.FlagReason);

        var foilLot = Assert.Single(lots, l => l.ProductId == foil.Id);
        Assert.Equal(12.00m, foilLot.UnitCost);
        Assert.True(foilLot.IsMissing);
        Assert.Equal(FlagReason.Manual, foilLot.FlagReason);
    }

    [Fact]
    public void MigrateDataIfNeeded_LegacyCardsTableMissingLateAddedColumns_MigratesWithoutThrowing()
    {
        // collection.db from a release that predates the ScanImagePath/Color/CardType/IsMissing/
        // FlagReason columns entirely (not merely NULL — the columns themselves don't exist). Before
        // the fix, the reader's fixed SELECT naming these columns threw SqliteException ("no such
        // column"), which (uncaught, in App.xaml.cs's startup Task.Run) crashed startup permanently,
        // since the failed migration never wrote the DB marker.
        CreateLegacyCollectionDbMissingOptionalColumns();
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(_tempDir, "collection.db")}"))
        {
            conn.Open();
            InsertCardMissingOptionalColumns(conn, CardGame.Mtg, "bolt-1", "Lightning Bolt", isFoil: false,
                setName: "Alpha", setCode: "lea", number: "1", rarity: "common",
                purchasePrice: 5.50m, dateAdded: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
        SqliteConnection.ClearAllPools();

        // Must not throw.
        var map = UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _unifiedFactory, NullLogger.Instance);
        Assert.Single(map);

        using var verify = NewUnifiedCtx();
        var product = Assert.Single(verify.Products.AsNoTracking().ToList());
        // Present columns still map correctly.
        Assert.Equal("Lightning Bolt", product.Name);
        Assert.Equal("Alpha", product.SetName);
        Assert.Equal("lea", product.SetCode);
        Assert.Equal("1", product.CollectorNumber);
        Assert.Equal("common", product.Rarity);
        // Columns absent from the legacy table default rather than throwing.
        Assert.Null(product.Color);
        Assert.Null(product.CardType);

        var lot = Assert.Single(verify.Lots.AsNoTracking().ToList());
        Assert.Equal(5.50m, lot.UnitCost);
        Assert.Null(lot.ScanImagePath);
        Assert.False(lot.IsMissing);
        Assert.Null(lot.FlagReason);
    }

    [Fact]
    public void MigrateDataIfNeeded_MapsContainerIdToLocationId_AndSeedsAcquireMovementPerLot()
    {
        CreateLegacyCollectionDb();
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(_tempDir, "collection.db")}"))
        {
            conn.Open();
            var oldContainerId = InsertContainer(conn, "Binder 1", ContainerType.Binder);
            InsertCard(conn, CardGame.OnePiece, "op-1", "Zoro", isFoil: false,
                setCode: "OP01", number: "1", rarity: "SR", containerId: oldContainerId,
                purchasePrice: 3.00m, dateAdded: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        }
        SqliteConnection.ClearAllPools();

        UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _unifiedFactory, NullLogger.Instance);

        using var verify = NewUnifiedCtx();
        var newContainer = Assert.Single(verify.StorageContainers.AsNoTracking().ToList());
        Assert.Equal("Binder 1", newContainer.Name);

        var lot = Assert.Single(verify.Lots.AsNoTracking().ToList());
        Assert.Equal(newContainer.Id, lot.LocationId);

        var movement = Assert.Single(verify.Movements.AsNoTracking().ToList());
        Assert.Equal(MovementType.Acquire, movement.Type);
        Assert.Equal(lot.Id, movement.LotId);
        Assert.Equal(lot.ProductId, movement.ProductId);
        Assert.Equal(1, movement.Quantity);
        Assert.Equal(lot.UnitCost, movement.UnitValue);

        // Acquire movement Timestamp is the recording time ("now"), NOT the lot's (here,
        // backdated) AcquisitionDate — matches InventoryService.AddLot's convention.
        Assert.NotEqual(lot.AcquisitionDate, movement.Timestamp);
        Assert.True(movement.Timestamp >= _testStart);
    }

    [Fact]
    public void MigrateDataIfNeeded_CopiesMismatchLogsAndScanDiagnosticEvents()
    {
        CreateLegacyCollectionDb();
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(_tempDir, "collection.db")}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO MismatchLogs
                    (ScanHash, OriginalCardId, OriginalName, OriginalSetCode, OriginalNumber, OriginalConfidence,
                     CorrectedCardId, CorrectedName, CorrectedSetCode, CorrectedNumber, CreatedAt)
                VALUES (12345, 'a', 'A', 's1', '1', 0.4, 'b', 'B', 's2', '2', '2026-01-01T00:00:00Z');
                INSERT INTO ScanDiagnosticEvents (SessionId, ScanHash, EventType, Timestamp, Payload)
                VALUES ('sess-1', 999, 'MatchAttempt', '2026-01-02T00:00:00Z', '{}');
                """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _unifiedFactory, NullLogger.Instance);

        using var verify = NewUnifiedCtx();
        var mismatch = Assert.Single(verify.MismatchLogs.AsNoTracking().ToList());
        Assert.Equal("A", mismatch.OriginalName);
        Assert.Equal("B", mismatch.CorrectedName);
        Assert.Equal(12345UL, mismatch.ScanHash);

        var diag = Assert.Single(verify.ScanDiagnosticEvents.AsNoTracking().ToList());
        Assert.Equal("sess-1", diag.SessionId);
        Assert.Equal("MatchAttempt", diag.EventType);
        Assert.Equal(999UL, diag.ScanHash);
    }

    [Fact]
    public void MigrateDataIfNeeded_RemapsEbayListingAndFlagResolutionCollectionCardId_ToNewLotId()
    {
        CreateLegacyCollectionDb();
        int oldCardId;
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(_tempDir, "collection.db")}"))
        {
            conn.Open();
            oldCardId = InsertCard(conn, CardGame.Mtg, "bolt-1", "Lightning Bolt", isFoil: false,
                setCode: "lea", number: "1", rarity: "common");

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO EbayListings (CollectionCardId, EbayItemId, Status, ListingType, ListedPrice, CreatedAt)
                VALUES ($cardId, 'item-1', 'Active', 'FixedPrice', '9.99', '2026-01-01T00:00:00Z');
                """;
            cmd.Parameters.AddWithValue("$cardId", oldCardId);
            cmd.ExecuteNonQuery();

            cmd.CommandText = """
                INSERT INTO FlagResolutions
                    (CollectionCardId, FlagReason, FixType, OriginalData, ResolvedData, ScanHash, FixedAt, CreatedAt)
                VALUES ($cardId, 'NoMatch', 'Manual', 'orig', 'resolved', 55, '2026-01-03T00:00:00Z', '2026-01-03T00:00:00Z');
                """;
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$cardId", oldCardId);
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var map = UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _unifiedFactory, NullLogger.Instance);

        Assert.True(map.TryGetValue(oldCardId, out var newLotId));

        using var verify = NewUnifiedCtx();
        var listing = Assert.Single(verify.EbayListings.AsNoTracking().ToList());
        Assert.Equal(newLotId, listing.LotId);
        Assert.Equal("item-1", listing.EbayItemId);

        var flag = Assert.Single(verify.FlagResolutions.AsNoTracking().ToList());
        Assert.Equal(newLotId, flag.LotId);
        Assert.Equal("NoMatch", flag.FlagReason);

        // Map is also persisted to disk, purely for external inspection/rollback tooling.
        var mapPath = Path.Combine(_tempDir, UnifiedMigrationService.CardToLotMapFileName);
        Assert.True(File.Exists(mapPath));
    }

    [Fact]
    public void MigrateDataIfNeeded_RemapsStorageContainerCoverCardId_ToNewLotId()
    {
        CreateLegacyCollectionDb();
        int oldCardId;
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(_tempDir, "collection.db")}"))
        {
            conn.Open();
            var containerId = InsertContainer(conn, "Binder 1", ContainerType.Binder);
            oldCardId = InsertCard(conn, CardGame.Mtg, "bolt-1", "Lightning Bolt", isFoil: false,
                setCode: "lea", number: "1", rarity: "common", containerId: containerId);

            // CoverCardId is a CollectionCard.Id in the old (Phase-1) schema; it must be remapped
            // to the new LotId, same as EbayListing/FlagResolution.CollectionCardId above.
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE StorageContainers SET CoverCardId = $cardId WHERE Id = $containerId";
            cmd.Parameters.AddWithValue("$cardId", oldCardId);
            cmd.Parameters.AddWithValue("$containerId", containerId);
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var map = UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _unifiedFactory, NullLogger.Instance);

        Assert.True(map.TryGetValue(oldCardId, out var newLotId));

        using var verify = NewUnifiedCtx();
        var newContainer = Assert.Single(verify.StorageContainers.AsNoTracking().ToList());
        Assert.Equal("Binder 1", newContainer.Name);
        Assert.Equal(newLotId, newContainer.CoverCardId);
    }

    [Fact]
    public void MigrateDataIfNeeded_SecondRun_IsNoOp()
    {
        CreateLegacyCollectionDb();
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(_tempDir, "collection.db")}"))
        {
            conn.Open();
            InsertCard(conn, CardGame.Mtg, "bolt-1", "Lightning Bolt", isFoil: false,
                setCode: "lea", number: "1", rarity: "common", purchasePrice: 5m);
        }
        SqliteConnection.ClearAllPools();

        var firstMap = UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _unifiedFactory, NullLogger.Instance);
        Assert.Single(firstMap);

        // The DB marker (not the file flag) is authoritative: delete the file flag to prove the
        // guard is really reading the committed DB marker, not the file.
        var flagPath = Path.Combine(_tempDir, UnifiedMigrationService.MigratedFlagFileName);
        Assert.True(File.Exists(flagPath));
        File.Delete(flagPath);

        var secondMap = UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _unifiedFactory, NullLogger.Instance);
        Assert.Empty(secondMap); // DB marker short-circuits; nothing new to report

        using var verify = NewUnifiedCtx();
        Assert.Single(verify.Products.AsNoTracking().ToList());
        Assert.Single(verify.Lots.AsNoTracking().ToList());
        Assert.Single(verify.Movements.AsNoTracking().ToList());
    }

    [Fact]
    public void MigrateDataIfNeeded_CommitsMigrationStateMarker_AtomicWithData()
    {
        CreateLegacyCollectionDb();
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(_tempDir, "collection.db")}"))
        {
            conn.Open();
            InsertCard(conn, CardGame.Mtg, "bolt-1", "Lightning Bolt", isFoil: false,
                setCode: "lea", number: "1", rarity: "common");
        }
        SqliteConnection.ClearAllPools();

        UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _unifiedFactory, NullLogger.Instance);

        using var verify = NewUnifiedCtx();
        var marker = Assert.Single(verify.MigrationState.AsNoTracking().ToList());
        Assert.Equal(UnifiedMigrationService.MigrationStateKey, marker.Key);
        Assert.True(marker.CompletedAt >= _testStart);
    }

    [Fact]
    public void MigrateDataIfNeeded_DoesNotOverwriteExistingPreMigrationBackup()
    {
        // Simulate a real inventory.db file plus a ".bak" already left behind by a prior
        // (e.g. crashed) migration attempt. On retry, the existing backup — the clean
        // pre-migration snapshot — must survive untouched rather than being overwritten by
        // whatever inventory.db looks like now.
        var inventoryDbPath = Path.Combine(_tempDir, "inventory.db");
        File.WriteAllText(inventoryDbPath, "current-inventory-db-contents");

        var backupPath = inventoryDbPath + ".pre-unified-migration.bak";
        const string originalBackupContents = "clean-pre-migration-snapshot";
        File.WriteAllText(backupPath, originalBackupContents);

        CreateLegacyCollectionDb();
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(_tempDir, "collection.db")}"))
        {
            conn.Open();
            InsertCard(conn, CardGame.Mtg, "bolt-1", "Lightning Bolt", isFoil: false,
                setCode: "lea", number: "1", rarity: "common");
        }
        SqliteConnection.ClearAllPools();

        UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _unifiedFactory, NullLogger.Instance);

        Assert.Equal(originalBackupContents, File.ReadAllText(backupPath));
    }

    [Fact]
    public void MigrateDataIfNeeded_NoCollectionDb_WritesFlagAndDoesNothing()
    {
        var noDbDir = Path.Combine(_tempDir, "no-collection-db");
        Directory.CreateDirectory(noDbDir);

        var map = UnifiedMigrationService.MigrateDataIfNeeded(noDbDir, _unifiedFactory, NullLogger.Instance);

        Assert.Empty(map);
        Assert.True(File.Exists(Path.Combine(noDbDir, UnifiedMigrationService.MigratedFlagFileName)));

        using var verify = NewUnifiedCtx();
        Assert.Empty(verify.Products.AsNoTracking().ToList());
    }

    [Fact]
    public void MigrateDataIfNeeded_CollectionDbWithNoCardsTable_WritesFlagAndDoesNothing()
    {
        // A collection.db file exists but has none of the legacy tables (e.g. a stray/empty file) —
        // must be treated the same as "no collection.db", not crash trying to SELECT from Cards.
        var dbPath = Path.Combine(_tempDir, "collection.db");
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE SomeUnrelatedTable (Id INTEGER PRIMARY KEY)";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var map = UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _unifiedFactory, NullLogger.Instance);

        Assert.Empty(map);
        Assert.True(File.Exists(Path.Combine(_tempDir, UnifiedMigrationService.MigratedFlagFileName)));

        using var verify = NewUnifiedCtx();
        Assert.Empty(verify.Products.AsNoTracking().ToList());
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
        UnifiedMigrationService.EnsureUnifiedSchema(emptyDir, NullLogger.Instance);

        // Real call: copy the Phase-1 db to the conventional "inventory.db" filename.
        var realDir = Path.Combine(_tempDir, "real");
        Directory.CreateDirectory(realDir);
        File.Copy(dbPath, Path.Combine(realDir, "inventory.db"));

        UnifiedMigrationService.EnsureUnifiedSchema(realDir, NullLogger.Instance);

        using var verifyConn = new SqliteConnection($"Data Source={Path.Combine(realDir, "inventory.db")}");
        verifyConn.Open();
        using var verifyCmd = verifyConn.CreateCommand();

        foreach (var table in new[] { "StorageContainers", "EbayListings", "MismatchLogs", "FlagResolutions", "ScanDiagnosticEvents", "MigrationState" })
        {
            verifyCmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'";
            Assert.Equal(1L, (long)verifyCmd.ExecuteScalar()!);
        }

        foreach (var (table, column) in new[] { ("Products", "SetName"), ("Products", "Color"), ("Products", "CardType"), ("Products", "LastMarketPrice"), ("Products", "PriceUpdatedAt"), ("Lots", "IsMissing"), ("Lots", "FlagReason") })
        {
            verifyCmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
            Assert.True((long)verifyCmd.ExecuteScalar()! > 0, $"{table}.{column} should exist");
        }

        // Idempotent — running again on the already-patched db must not throw.
        SqliteConnection.ClearAllPools();
        UnifiedMigrationService.EnsureUnifiedSchema(realDir, NullLogger.Instance);

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

        UnifiedMigrationService.EnsureUnifiedSchema(dir, NullLogger.Instance);

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
        UnifiedMigrationService.EnsureUnifiedSchema(dir, NullLogger.Instance);
        SqliteConnection.ClearAllPools();
    }

    private class UnifiedDbContextFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
