using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Interfaces;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class CollectionCardCrudTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CollectionDbContext> _options;

    public CollectionCardCrudTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new CollectionDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void UpdateCollectionCard_PersistsChanges()
    {
        // Seed a card
        using (var ctx = new CollectionDbContext(_options))
        {
            ctx.Cards.Add(new CollectionCard
            {
                Game = CardGame.Mtg,
                GameCardId = "id1",
                Name = "Lightning Bolt",
                SetName = "Alpha",
                SetCode = "lea",
                Number = "1",
                Rarity = "common",
                Condition = "NM",
            });
            ctx.SaveChanges();
        }

        var service = CreateService();

        // Read the card, modify it, update
        CollectionCard card;
        using (var ctx = new CollectionDbContext(_options))
        {
            card = ctx.Cards.Single();
        }

        card.Condition = "LP";
        card.IsFoil = true;
        card.PurchasePrice = 5.99m;
        service.UpdateCollectionCard(card);

        // Verify
        using (var ctx = new CollectionDbContext(_options))
        {
            var updated = ctx.Cards.AsNoTracking().Single();
            Assert.Equal("LP", updated.Condition);
            Assert.True(updated.IsFoil);
            Assert.Equal(5.99m, updated.PurchasePrice);
        }
    }

    [Fact]
    public void UpdateCollectionCard_CanReassignCard()
    {
        using (var ctx = new CollectionDbContext(_options))
        {
            ctx.Cards.Add(new CollectionCard
            {
                Game = CardGame.Mtg,
                GameCardId = "old-id",
                Name = "Wrong Card",
                SetName = "Alpha",
                SetCode = "lea",
                Number = "1",
                Rarity = "common",
            });
            ctx.SaveChanges();
        }

        var service = CreateService();

        CollectionCard card;
        using (var ctx = new CollectionDbContext(_options))
        {
            card = ctx.Cards.Single();
        }

        // Reassign to a different card
        card.GameCardId = "new-id";
        card.Name = "Correct Card";
        card.SetName = "Beta";
        card.SetCode = "leb";
        card.Number = "2";
        card.Rarity = "rare";
        card.ImageUri = "https://img/correct.jpg";
        service.UpdateCollectionCard(card);

        using (var ctx = new CollectionDbContext(_options))
        {
            var updated = ctx.Cards.AsNoTracking().Single();
            Assert.Equal("new-id", updated.GameCardId);
            Assert.Equal("Correct Card", updated.Name);
            Assert.Equal("Beta", updated.SetName);
        }
    }

    [Fact]
    public void DeleteCollectionCard_RemovesFromDb()
    {
        int cardId;
        using (var ctx = new CollectionDbContext(_options))
        {
            var card = new CollectionCard
            {
                Game = CardGame.Mtg,
                GameCardId = "id1",
                Name = "Test Card",
            };
            ctx.Cards.Add(card);
            ctx.SaveChanges();
            cardId = card.Id;
        }

        var service = CreateService();
        service.DeleteCollectionCard(cardId);

        using (var ctx = new CollectionDbContext(_options))
        {
            Assert.Empty(ctx.Cards.ToList());
        }
    }

    [Fact]
    public void DeleteCollectionCard_NonExistentId_DoesNotThrow()
    {
        var service = CreateService();
        service.DeleteCollectionCard(99999);
        // Should not throw
    }

    private CardSevice CreateService()
    {
        return new CardSevice(
            new StubHashService(),
            [],
            new MockCollectionDbContextFactory(_options),
            new StubOcrService(),
            new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
            NullLogger<CardSevice>.Instance,
            new DataPathService(Path.GetTempPath()),
            new NullScanDiagnosticService(),
            new NullAuditService());
    }

    private class StubHashService : IPerceptualHashService
    {
        public ulong ComputeHash(System.IO.Stream imageStream, Action<HashStageResult>? onStage = null) => 0;
        public ulong[] ComputeArtHash(System.IO.Stream imageStream, (double X, double Y, double W, double H)[] cropRegions, Action<HashStageResult>? onStage = null) => new ulong[cropRegions.Length];
    }

    private class StubOcrService : IOcrMatchingService
    {
        public Dictionary<string, ulong> SymbolHashes { get; set; } = [];
        public Task<OcrMatchResult> AnalyzeCardAsync(byte[] imageData) => Task.FromResult(new OcrMatchResult());
        public (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] imageData) => ([], 0);
    }

    private class NullScanDiagnosticService : IScanDiagnosticService
    {
        public void LogScanCompleted(string sessionId, ulong scanHash, CardMatch? match, MatchDiagnostics? diagnostics, ulong[]? artHashes, OcrMatchResult? ocrResult, FlagReason autoFlagReason) { }
        public void LogUserFlagged(ulong scanHash, ScannedCard card) { }
        public void LogUserConfirmed(ulong scanHash, ScannedCard card) { }
        public void LogUserCorrected(ulong scanHash, ScannedCard card, CardMatch newMatch) { }
        public void LogUserUnflagged(ulong scanHash, ScannedCard card, FlagReason previousReason) { }
        public void ExportDiagnostics(string filePath) { }
        public void ClearDiagnostics() { }
        public int GetEventCount() => 0;
    }

    private class MockCollectionDbContextFactory(DbContextOptions<CollectionDbContext> options) : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }

    private class NullAuditService : IAuditService
    {
        public bool IsAuditActive => false;
        public int? AuditLocationId => null;
        public string? AuditLocationName => null;
        public void StartAudit(int containerId) { }
        public void EndAudit() { }
        public CardMatch? FindScopedMatch(ulong hash, ulong[]? artHashes) => null;
        public AuditReport GenerateReport(IEnumerable<ScannedCard> scannedCards) => throw new NotImplementedException();
    }
}
