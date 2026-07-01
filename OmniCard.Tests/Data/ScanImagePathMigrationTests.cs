using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Data;

public class ScanImagePathMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public ScanImagePathMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_collection_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public void ScanImagePath_CanBeStoredAndRetrieved()
    {
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        using (var ctx = new CollectionDbContext(options))
        {
            ctx.Database.EnsureCreated();
            ctx.Cards.Add(new CollectionCard
            {
                Game = CardGame.Mtg,
                GameCardId = "test-id",
                Name = "Test Card",
                ScanImagePath = "scans/1.png",
            });
            ctx.SaveChanges();
        }

        using (var ctx = new CollectionDbContext(options))
        {
            var card = ctx.Cards.AsNoTracking().Single();
            Assert.Equal("scans/1.png", card.ScanImagePath);
        }
    }

    [Fact]
    public void ScanImagePath_NullByDefault()
    {
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        using (var ctx = new CollectionDbContext(options))
        {
            ctx.Database.EnsureCreated();
            ctx.Cards.Add(new CollectionCard
            {
                Game = CardGame.Mtg,
                GameCardId = "test-id",
                Name = "Test Card",
            });
            ctx.SaveChanges();
        }

        using (var ctx = new CollectionDbContext(options))
        {
            var card = ctx.Cards.AsNoTracking().Single();
            Assert.Null(card.ScanImagePath);
        }
    }
}
