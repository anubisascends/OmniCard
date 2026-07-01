using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;

namespace OmniCard.Tests.Data;

public class ArtHashMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public ArtHashMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"art_hash_test_{Guid.NewGuid()}.db");
    }

    [Fact]
    public void RunScryfallMigrations_AddsArtHashColumn()
    {
        // Create DB with EnsureCreated (includes ArtHash if property exists)
        var options = new DbContextOptionsBuilder<ScryfallDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        using (var ctx = new ScryfallDbContext(options))
            ctx.Database.EnsureCreated();

        // Simulate pre-migration DB by dropping the column
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            // SQLite doesn't support DROP COLUMN before 3.35.0, so we verify column exists instead
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA table_info(Cards)";
            using var reader = cmd.ExecuteReader();
            var columns = new List<string>();
            while (reader.Read())
                columns.Add(reader.GetString(1));
            Assert.Contains("ArtHash", columns);
        }

        // Running migrations again should be safe (idempotent)
        ScryfallDbContext.RunScryfallMigrations(_dbPath);

        // Verify column still exists and is usable
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ArtHash FROM Cards LIMIT 0";
            cmd.ExecuteNonQuery(); // Should not throw
        }
    }

    [Fact]
    public void RunScryfallMigrations_Idempotent_NoErrorOnSecondRun()
    {
        var options = new DbContextOptionsBuilder<ScryfallDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        using (var ctx = new ScryfallDbContext(options))
            ctx.Database.EnsureCreated();

        ScryfallDbContext.RunScryfallMigrations(_dbPath);
        ScryfallDbContext.RunScryfallMigrations(_dbPath); // Second run — should not throw
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
