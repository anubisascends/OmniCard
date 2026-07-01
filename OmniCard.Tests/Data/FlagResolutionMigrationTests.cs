using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Data;

public class FlagResolutionMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CollectionDbContext> _options;

    public FlagResolutionMigrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    [Fact]
    public void FlagResolutions_TableCreatedByEnsureCreated()
    {
        using var ctx = new CollectionDbContext(_options);
        ctx.Database.EnsureCreated();

        // Verify table exists by inserting and querying
        var card = new CollectionCard
        {
            Game = CardGame.Mtg,
            Name = "Test Card",
            SetCode = "tst",
            SetName = "Test Set",
            Number = "001",
            Rarity = "common",
            GameCardId = "test-001",
        };
        ctx.Cards.Add(card);
        ctx.SaveChanges();

        var resolution = new FlagResolution
        {
            CollectionCardId = card.Id,
            FlagReason = "NoMatch",
            FixType = "CardReassign",
            OriginalData = "{}",
            ResolvedData = "{}",
            ScanHash = 0x1234,
            Confidence = null,
            FixedAt = DateTime.UtcNow,
        };
        ctx.FlagResolutions.Add(resolution);
        ctx.SaveChanges();

        var loaded = ctx.FlagResolutions.First();
        Assert.Equal("NoMatch", loaded.FlagReason);
        Assert.Equal("CardReassign", loaded.FixType);
        Assert.Equal(card.Id, loaded.CollectionCardId);
    }

    [Fact]
    public void FlagResolution_CascadeDeletesWithCard()
    {
        using var ctx = new CollectionDbContext(_options);
        ctx.Database.EnsureCreated();

        var card = new CollectionCard
        {
            Game = CardGame.Mtg,
            Name = "Test Card",
            SetCode = "tst",
            SetName = "Test Set",
            Number = "001",
            Rarity = "common",
            GameCardId = "test-001",
        };
        ctx.Cards.Add(card);
        ctx.SaveChanges();

        ctx.FlagResolutions.Add(new FlagResolution
        {
            CollectionCardId = card.Id,
            FlagReason = "VeryLowConfidence",
            FixType = "MatchConfirmed",
            OriginalData = "{}",
            ResolvedData = "{}",
            ScanHash = 0xABCD,
            Confidence = 15.0,
            FixedAt = DateTime.UtcNow,
        });
        ctx.SaveChanges();

        Assert.Single(ctx.FlagResolutions);

        ctx.Cards.Remove(card);
        ctx.SaveChanges();

        Assert.Empty(ctx.FlagResolutions);
    }

    public void Dispose() => _connection.Dispose();
}
