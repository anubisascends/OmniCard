using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class CollectionMigrationTests : IDisposable
{
    private readonly string _tempDir;

    public CollectionMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        // Clear SQLite connection pools so file handles are released before deleting temp dir
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void MigrateIfNeeded_MigratesOldMtgAndOptcgData()
    {
        // Seed the old MTG collection DB with the OLD schema
        var oldCollectionPath = Path.Combine(_tempDir, "collection.db");
        SeedOldMtgCollection(oldCollectionPath);

        // Seed the old OPTCG collection DB
        var oldOptcgPath = Path.Combine(_tempDir, "optcg_collection.db");
        SeedOldOptcgCollection(oldOptcgPath);

        // Clear SQLite pools so the seeded files are not locked before we rename/recreate
        SqliteConnection.ClearAllPools();

        // Rename old collection.db to the .bak name (simulates what the app does on schema upgrade)
        var oldMtgBackupPath = Path.Combine(_tempDir, "collection.db.old-mtg.bak");
        File.Move(oldCollectionPath, oldMtgBackupPath, overwrite: true);

        // Create the new collection DB with new schema
        var newOptions = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite($"Data Source={oldCollectionPath}")
            .Options;
        using (var ctx = new CollectionDbContext(newOptions))
        {
            ctx.Database.EnsureCreated();
        }

        var factory = new FileDbContextFactory(newOptions);

        // Act
        CollectionMigrationService.MigrateIfNeeded(
            _tempDir, factory, NullLogger.Instance);

        // Assert
        using var verifyCtx = new CollectionDbContext(newOptions);
        var cards = verifyCtx.Cards.AsNoTracking().OrderBy(c => c.Name).ToList();
        Assert.Equal(2, cards.Count);

        var bolt = cards[0];
        Assert.Equal("Lightning Bolt", bolt.Name);
        Assert.Equal(CardGame.Mtg, bolt.Game);

        var zoro = cards[1];
        Assert.Equal("Roronoa Zoro", zoro.Name);
        Assert.Equal(CardGame.OnePiece, zoro.Game);
        Assert.Equal("OP01-001", zoro.GameCardId);
    }

    [Fact]
    public void MigrateIfNeeded_NoOldFiles_DoesNothing()
    {
        var collectionPath = Path.Combine(_tempDir, "collection.db");
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite($"Data Source={collectionPath}")
            .Options;
        using (var ctx = new CollectionDbContext(options))
        {
            ctx.Database.EnsureCreated();
        }

        var factory = new FileDbContextFactory(options);

        // Act — no old files exist, should be a no-op
        CollectionMigrationService.MigrateIfNeeded(
            _tempDir, factory, NullLogger.Instance);

        using var verifyCtx = new CollectionDbContext(options);
        Assert.Empty(verifyCtx.Cards.ToList());
    }

    private void SeedOldMtgCollection(string dbPath)
    {
        // Create old-schema DB using raw SQL (old schema had ScryfallId, ManaCost, TypeLine, etc.)
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Cards (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ScryfallId TEXT NOT NULL,
                Name TEXT NOT NULL DEFAULT '',
                SetName TEXT NOT NULL DEFAULT '',
                SetCode TEXT NOT NULL DEFAULT '',
                Number TEXT NOT NULL DEFAULT '',
                CollectorNumber TEXT NOT NULL DEFAULT '',
                Rarity TEXT NOT NULL DEFAULT '',
                ImageUri TEXT,
                Condition TEXT NOT NULL DEFAULT 'NM',
                IsFoil INTEGER NOT NULL DEFAULT 0,
                PurchasePrice TEXT,
                DateAdded TEXT NOT NULL,
                ManaCost TEXT,
                TypeLine TEXT NOT NULL DEFAULT '',
                OracleText TEXT
            );
            INSERT INTO Cards (ScryfallId, Name, SetName, SetCode, Number, CollectorNumber, Rarity, ImageUri, Condition, IsFoil, DateAdded, ManaCost, TypeLine)
            VALUES ('0000579f-7b35-4ed3-b44c-db2a538066fe', 'Lightning Bolt', 'Alpha', 'lea', '1', '1', 'common', 'https://img/bolt.jpg', 'NM', 0, '2026-01-01T00:00:00Z', '{R}', 'Instant');
            """;
        cmd.ExecuteNonQuery();
    }

    private void SeedOldOptcgCollection(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Cards (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CardSetId TEXT NOT NULL DEFAULT '',
                Name TEXT NOT NULL DEFAULT '',
                SetName TEXT NOT NULL DEFAULT '',
                SetCode TEXT NOT NULL DEFAULT '',
                Number TEXT NOT NULL DEFAULT '',
                Rarity TEXT NOT NULL DEFAULT '',
                ImageUri TEXT,
                Condition TEXT NOT NULL DEFAULT 'NM',
                IsFoil INTEGER NOT NULL DEFAULT 0,
                PurchasePrice TEXT,
                DateAdded TEXT NOT NULL,
                CardColor TEXT NOT NULL DEFAULT '',
                CardType TEXT NOT NULL DEFAULT '',
                CardCost TEXT,
                CardPower TEXT,
                CardText TEXT
            );
            INSERT INTO Cards (CardSetId, Name, SetName, SetCode, Number, Rarity, ImageUri, Condition, IsFoil, DateAdded, CardColor, CardType)
            VALUES ('OP01-001', 'Roronoa Zoro', 'Romance Dawn', 'OP01', 'OP01-001', 'SR', 'https://img/zoro.jpg', 'NM', 0, '2026-01-01T00:00:00Z', 'Green', 'Character');
            """;
        cmd.ExecuteNonQuery();
    }

    private class FileDbContextFactory(DbContextOptions<CollectionDbContext> options) : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }
}
