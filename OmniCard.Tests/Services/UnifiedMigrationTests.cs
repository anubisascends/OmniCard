using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class UnifiedMigrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteConnection _collectionConnection;
    private readonly SqliteConnection _unifiedConnection;
    private readonly DbContextOptions<CollectionDbContext> _collectionOptions;
    private readonly DbContextOptions<OmniCardDbContext> _unifiedOptions;
    private readonly CollectionDbContextFactory _collectionFactory;
    private readonly UnifiedDbContextFactory _unifiedFactory;

    public UnifiedMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardUnifiedMigrationTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _collectionConnection = new SqliteConnection("Data Source=:memory:");
        _collectionConnection.Open();
        _collectionOptions = new DbContextOptionsBuilder<CollectionDbContext>().UseSqlite(_collectionConnection).Options;
        using (var ctx = new CollectionDbContext(_collectionOptions))
            ctx.Database.EnsureCreated();
        _collectionFactory = new CollectionDbContextFactory(_collectionOptions);

        _unifiedConnection = new SqliteConnection("Data Source=:memory:");
        _unifiedConnection.Open();
        _unifiedOptions = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_unifiedConnection).Options;
        using (var ctx = new OmniCardDbContext(_unifiedOptions))
            ctx.Database.EnsureCreated();
        _unifiedFactory = new UnifiedDbContextFactory(_unifiedOptions);

        // Marks collection.db as "present" so MigrateDataIfNeeded doesn't short-circuit
        // (it only checks File.Exists for the flag file / collection.db marker paths, the
        // actual DB content comes from the in-memory connections above).
        File.WriteAllText(Path.Combine(_tempDir, "collection.db"), "");
    }

    public void Dispose()
    {
        _collectionConnection.Dispose();
        _unifiedConnection.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private CollectionDbContext NewCollectionCtx() => new(_collectionOptions);
    private OmniCardDbContext NewUnifiedCtx() => new(_unifiedOptions);

    [Fact]
    public void MigrateDataIfNeeded_DedupsProductsByGameCardIdFoil_AndMapsLotFields()
    {
        using (var ctx = NewCollectionCtx())
        {
            ctx.Cards.AddRange(
                new CollectionCard
                {
                    Game = CardGame.Mtg, GameCardId = "bolt-1", Name = "Lightning Bolt", SetName = "Alpha",
                    SetCode = "lea", Number = "1", Rarity = "common", Color = "R", CardType = "Instant",
                    ImageUri = "img/bolt.jpg", IsFoil = false, Condition = "NM", PurchasePrice = 5.50m,
                    DateAdded = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    ScanImagePath = "scans/bolt1.jpg", Page = 2, Slot = 3, Section = "A",
                    IsMissing = false, FlagReason = FlagReason.None,
                },
                // Second copy of the same card/foil -> same Product, separate Lot.
                new CollectionCard
                {
                    Game = CardGame.Mtg, GameCardId = "bolt-1", Name = "Lightning Bolt", SetName = "Alpha",
                    SetCode = "lea", Number = "1", Rarity = "common", IsFoil = false, Condition = "LP",
                    PurchasePrice = 4.00m, DateAdded = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                },
                // Foil variant of the same card -> distinct Product.
                new CollectionCard
                {
                    Game = CardGame.Mtg, GameCardId = "bolt-1", Name = "Lightning Bolt", SetName = "Alpha",
                    SetCode = "lea", Number = "1", Rarity = "common", IsFoil = true, Condition = "NM",
                    PurchasePrice = 12.00m, DateAdded = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    IsMissing = true, FlagReason = FlagReason.Manual,
                });
            ctx.SaveChanges();
        }

        UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _collectionFactory, _unifiedFactory, NullLogger.Instance);

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
    public void MigrateDataIfNeeded_MapsContainerIdToLocationId_AndSeedsAcquireMovementPerLot()
    {
        int oldContainerId;
        using (var ctx = NewCollectionCtx())
        {
            var container = new StorageContainer { Name = "Binder 1", ContainerType = ContainerType.Binder, SortOrder = 1 };
            ctx.StorageContainers.Add(container);
            ctx.SaveChanges();
            oldContainerId = container.Id;

            ctx.Cards.Add(new CollectionCard
            {
                Game = CardGame.OnePiece, GameCardId = "op-1", Name = "Zoro", SetCode = "OP01", Number = "1",
                Rarity = "SR", IsFoil = false, ContainerId = oldContainerId, PurchasePrice = 3.00m,
                DateAdded = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            ctx.SaveChanges();
        }

        UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _collectionFactory, _unifiedFactory, NullLogger.Instance);

        using var verify = NewUnifiedCtx();
        var newContainer = Assert.Single(verify.StorageContainers.AsNoTracking().ToList());
        Assert.Equal("Binder 1", newContainer.Name);

        var lot = Assert.Single(verify.Lots.AsNoTracking().ToList());
        Assert.Equal(newContainer.Id, lot.LocationId);
        // Container ids are remapped, not preserved verbatim, across the two stores.
        Assert.NotEqual(0, oldContainerId);

        var movement = Assert.Single(verify.Movements.AsNoTracking().ToList());
        Assert.Equal(MovementType.Acquire, movement.Type);
        Assert.Equal(lot.Id, movement.LotId);
        Assert.Equal(lot.ProductId, movement.ProductId);
        Assert.Equal(1, movement.Quantity);
        Assert.Equal(lot.UnitCost, movement.UnitValue);
        Assert.Equal(lot.AcquisitionDate, movement.Timestamp);
    }

    [Fact]
    public void MigrateDataIfNeeded_CopiesMismatchLogsAndScanDiagnosticEvents()
    {
        using (var ctx = NewCollectionCtx())
        {
            ctx.MismatchLogs.Add(new MismatchLog
            {
                ScanHash = 12345, OriginalCardId = "a", OriginalName = "A", OriginalSetCode = "s1",
                OriginalNumber = "1", OriginalConfidence = 0.4, CorrectedCardId = "b", CorrectedName = "B",
                CorrectedSetCode = "s2", CorrectedNumber = "2", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            ctx.ScanDiagnosticEvents.Add(new ScanDiagnosticEvent
            {
                SessionId = "sess-1", ScanHash = 999, EventType = "MatchAttempt",
                Timestamp = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), Payload = "{}",
            });
            ctx.SaveChanges();
        }

        UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _collectionFactory, _unifiedFactory, NullLogger.Instance);

        using var verify = NewUnifiedCtx();
        var mismatch = Assert.Single(verify.MismatchLogs.AsNoTracking().ToList());
        Assert.Equal("A", mismatch.OriginalName);
        Assert.Equal("B", mismatch.CorrectedName);

        var diag = Assert.Single(verify.ScanDiagnosticEvents.AsNoTracking().ToList());
        Assert.Equal("sess-1", diag.SessionId);
        Assert.Equal("MatchAttempt", diag.EventType);
    }

    [Fact]
    public void MigrateDataIfNeeded_RemapsEbayListingAndFlagResolutionCollectionCardId_ToNewLotId()
    {
        int oldCardId;
        using (var ctx = NewCollectionCtx())
        {
            var card = new CollectionCard
            {
                Game = CardGame.Mtg, GameCardId = "bolt-1", Name = "Lightning Bolt", SetCode = "lea",
                Number = "1", Rarity = "common", IsFoil = false,
            };
            ctx.Cards.Add(card);
            ctx.SaveChanges();
            oldCardId = card.Id;

            ctx.EbayListings.Add(new EbayListing
            {
                CollectionCardId = oldCardId, EbayItemId = "item-1", Status = EbayListingStatus.Active,
                ListingType = EbayListingType.FixedPrice, ListedPrice = 9.99m,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            ctx.FlagResolutions.Add(new FlagResolution
            {
                CollectionCardId = oldCardId, FlagReason = "NoMatch", FixType = "Manual",
                OriginalData = "orig", ResolvedData = "resolved", ScanHash = 55,
                FixedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            });
            ctx.SaveChanges();
        }

        var map = UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _collectionFactory, _unifiedFactory, NullLogger.Instance);

        Assert.True(map.TryGetValue(oldCardId, out var newLotId));

        using var verify = NewUnifiedCtx();
        var listing = Assert.Single(verify.EbayListings.AsNoTracking().ToList());
        Assert.Equal(newLotId, listing.CollectionCardId);
        Assert.Equal("item-1", listing.EbayItemId);

        var flag = Assert.Single(verify.FlagResolutions.AsNoTracking().ToList());
        Assert.Equal(newLotId, flag.CollectionCardId);
        Assert.Equal("NoMatch", flag.FlagReason);

        // Map is also persisted to disk for a later process (Task 5) to consume.
        var mapPath = Path.Combine(_tempDir, UnifiedMigrationService.CardToLotMapFileName);
        Assert.True(File.Exists(mapPath));
    }

    [Fact]
    public void MigrateDataIfNeeded_SecondRun_IsNoOp()
    {
        using (var ctx = NewCollectionCtx())
        {
            ctx.Cards.Add(new CollectionCard
            {
                Game = CardGame.Mtg, GameCardId = "bolt-1", Name = "Lightning Bolt", SetCode = "lea",
                Number = "1", Rarity = "common", IsFoil = false, PurchasePrice = 5m,
            });
            ctx.SaveChanges();
        }

        var firstMap = UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _collectionFactory, _unifiedFactory, NullLogger.Instance);
        Assert.Single(firstMap);

        var secondMap = UnifiedMigrationService.MigrateDataIfNeeded(_tempDir, _collectionFactory, _unifiedFactory, NullLogger.Instance);
        Assert.Empty(secondMap); // flag file short-circuits; nothing new to report

        using var verify = NewUnifiedCtx();
        Assert.Single(verify.Products.AsNoTracking().ToList());
        Assert.Single(verify.Lots.AsNoTracking().ToList());
        Assert.Single(verify.Movements.AsNoTracking().ToList());
    }

    [Fact]
    public void MigrateDataIfNeeded_NoCollectionDb_WritesFlagAndDoesNothing()
    {
        var noDbDir = Path.Combine(_tempDir, "no-collection-db");
        Directory.CreateDirectory(noDbDir);

        var map = UnifiedMigrationService.MigrateDataIfNeeded(noDbDir, _collectionFactory, _unifiedFactory, NullLogger.Instance);

        Assert.Empty(map);
        Assert.True(File.Exists(Path.Combine(noDbDir, UnifiedMigrationService.MigratedFlagFileName)));

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

        foreach (var table in new[] { "StorageContainers", "EbayListings", "MismatchLogs", "FlagResolutions", "ScanDiagnosticEvents" })
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
        UnifiedMigrationService.EnsureUnifiedSchema(realDir, NullLogger.Instance);

        verifyConn.Dispose();
        SqliteConnection.ClearAllPools();
    }

    private class CollectionDbContextFactory(DbContextOptions<CollectionDbContext> options) : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }

    private class UnifiedDbContextFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
