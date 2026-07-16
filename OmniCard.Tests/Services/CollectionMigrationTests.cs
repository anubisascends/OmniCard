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

    [Fact]
    public void RepairOptcgSetCodes_RewritesNonCanonicalCodesFromReference()
    {
        var collectionPath = Path.Combine(_tempDir, "collection.db");
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite($"Data Source={collectionPath}")
            .Options;

        using (var ctx = new CollectionDbContext(options))
        {
            ctx.Database.EnsureCreated();
            ctx.Cards.AddRange(
                // Hyphenated legacy code -> should become OP16
                new CollectionCard { Game = CardGame.OnePiece, GameCardId = "OP16-066", SetCode = "OP-16", SetName = "wrong", Number = "OP16-066", Name = "Sengoku" },
                // Bogus composite code, card actually belongs to OP14
                new CollectionCard { Game = CardGame.OnePiece, GameCardId = "OP14-086", SetCode = "OP14-EB04", SetName = "wrong", Number = "OP14-086", Name = "Miss Doublefinger" },
                // Same composite code, card actually belongs to EB04
                new CollectionCard { Game = CardGame.OnePiece, GameCardId = "EB04-046", SetCode = "OP15-EB04", SetName = "wrong", Number = "EB04-046", Name = "Doll" },
                // Already correct -> untouched
                new CollectionCard { Game = CardGame.OnePiece, GameCardId = "OP16-100", SetCode = "OP16", SetName = "One Piece 16", Number = "OP16-100", Name = "Correct Card" },
                // Unknown placeholder (empty ids) -> untouched
                new CollectionCard { Game = CardGame.OnePiece, GameCardId = "", SetCode = "", SetName = "", Number = "", Name = "Unknown Card" },
                // GameCardId not in reference -> untouched
                new CollectionCard { Game = CardGame.OnePiece, GameCardId = "ZZ99-001", SetCode = "ZZ-99", SetName = "wrong", Number = "ZZ99-001", Name = "Orphan" },
                // MTG row with a hyphenated-looking code -> untouched (repair is OnePiece-only)
                new CollectionCard { Game = CardGame.Mtg, GameCardId = "abc", SetCode = "lea", SetName = "Alpha", Number = "1", Name = "Lightning Bolt" });
            ctx.SaveChanges();
        }

        // Seed the canonical reference optcg.db
        SeedReferenceOptcg(Path.Combine(_tempDir, "optcg.db"));
        SqliteConnection.ClearAllPools();

        var factory = new FileDbContextFactory(options);

        // Act
        var repaired = CollectionMigrationService.RepairOptcgSetCodes(_tempDir, factory, NullLogger.Instance);

        // Assert — 3 rows corrected
        Assert.Equal(3, repaired);

        using var verify = new CollectionDbContext(options);
        var byName = verify.Cards.AsNoTracking().ToDictionary(c => c.Name);

        Assert.Equal("OP16", byName["Sengoku"].SetCode);
        Assert.Equal("One Piece 16", byName["Sengoku"].SetName);
        Assert.Equal("OP14", byName["Miss Doublefinger"].SetCode);
        Assert.Equal("One Piece 14", byName["Miss Doublefinger"].SetName);
        Assert.Equal("EB04", byName["Doll"].SetCode);
        Assert.Equal("Extra Booster 4", byName["Doll"].SetName);

        // Untouched rows
        Assert.Equal("OP16", byName["Correct Card"].SetCode);
        Assert.Equal("", byName["Unknown Card"].SetCode);
        Assert.Equal("ZZ-99", byName["Orphan"].SetCode);
        Assert.Equal("lea", byName["Lightning Bolt"].SetCode);

        // Idempotent — a second run repairs nothing
        var again = CollectionMigrationService.RepairOptcgSetCodes(_tempDir, factory, NullLogger.Instance);
        Assert.Equal(0, again);
    }

    [Fact]
    public void RepairOptcgSetCodes_NoReferenceDb_IsNoOp()
    {
        var collectionPath = Path.Combine(_tempDir, "collection.db");
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite($"Data Source={collectionPath}")
            .Options;
        using (var ctx = new CollectionDbContext(options))
        {
            ctx.Database.EnsureCreated();
            ctx.Cards.Add(new CollectionCard { Game = CardGame.OnePiece, GameCardId = "OP16-066", SetCode = "OP-16", SetName = "wrong", Number = "OP16-066", Name = "Sengoku" });
            ctx.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        var factory = new FileDbContextFactory(options);

        // No optcg.db present — must not throw, must change nothing
        var repaired = CollectionMigrationService.RepairOptcgSetCodes(_tempDir, factory, NullLogger.Instance);
        Assert.Equal(0, repaired);

        using var verify = new CollectionDbContext(options);
        Assert.Equal("OP-16", verify.Cards.AsNoTracking().Single().SetCode);
    }

    private void SeedReferenceOptcg(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Cards (
                CardSetId TEXT NOT NULL,
                CardName TEXT NOT NULL DEFAULT '',
                SetId TEXT NOT NULL DEFAULT '',
                SetName TEXT NOT NULL DEFAULT '',
                CardNumber TEXT NOT NULL DEFAULT ''
            );
            INSERT INTO Cards (CardSetId, CardName, SetId, SetName, CardNumber) VALUES
                ('OP16-066', 'Sengoku', 'OP16', 'One Piece 16', '066'),
                ('OP16-100', 'Correct Card', 'OP16', 'One Piece 16', '100'),
                ('OP14-086', 'Miss Doublefinger', 'OP14', 'One Piece 14', '086'),
                ('EB04-046', 'Doll', 'EB04', 'Extra Booster 4', '046');
            """;
        cmd.ExecuteNonQuery();
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
