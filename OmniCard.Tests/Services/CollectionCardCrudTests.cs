using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Models;
using OmniCard.Interfaces;
using OmniCard.Collection;

namespace OmniCard.Tests.Services;

public class CollectionCardCrudTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CollectionDbContext> _options;
    private readonly SqliteConnection _omniConnection;
    private readonly DbContextOptions<OmniCardDbContext> _omniOptions;

    public CollectionCardCrudTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new CollectionDbContext(_options);
        ctx.Database.EnsureCreated();

        _omniConnection = new SqliteConnection("Data Source=:memory:");
        _omniConnection.Open();
        _omniOptions = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_omniConnection)
            .Options;
        using var omniCtx = new OmniCardDbContext(_omniOptions);
        omniCtx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        _omniConnection.Dispose();
    }

    [Fact]
    public void UpdateCollectionCard_PersistsChanges()
    {
        // Seed a Product + Lot (the unified-store equivalent of a CollectionCard row)
        int lotId;
        using (var ctx = new OmniCardDbContext(_omniOptions))
        {
            var product = new Product
            {
                Game = CardGame.Mtg,
                Category = ProductCategory.Single,
                GameCardId = "id1",
                Name = "Lightning Bolt",
                SetName = "Alpha",
                SetCode = "lea",
                CollectorNumber = "1",
                Rarity = "common",
            };
            ctx.Products.Add(product);
            ctx.SaveChanges();

            var lot = new InventoryLot { ProductId = product.Id, Condition = "NM" };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();
            lotId = lot.Id;
        }

        var service = CreateService();

        var card = service.GetCollectionCards([lotId]).Single();
        card.Condition = "LP";
        card.IsFoil = true;
        card.PurchasePrice = 5.99m;
        service.UpdateCollectionCard(card);

        // Verify
        using (var ctx = new OmniCardDbContext(_omniOptions))
        {
            var updatedLot = ctx.Lots.AsNoTracking().Include(l => l.Product).Single(l => l.Id == lotId);
            Assert.Equal("LP", updatedLot.Condition);
            Assert.True(updatedLot.Product.Foil);
            Assert.Equal(5.99m, updatedLot.UnitCost);
        }
    }

    [Fact]
    public void UpdateCollectionCard_CanReassignCard()
    {
        int lotId;
        using (var ctx = new OmniCardDbContext(_omniOptions))
        {
            var product = new Product
            {
                Game = CardGame.Mtg,
                Category = ProductCategory.Single,
                GameCardId = "old-id",
                Name = "Wrong Card",
                SetName = "Alpha",
                SetCode = "lea",
                CollectorNumber = "1",
                Rarity = "common",
            };
            ctx.Products.Add(product);
            ctx.SaveChanges();

            var lot = new InventoryLot { ProductId = product.Id };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();
            lotId = lot.Id;
        }

        var service = CreateService();

        var card = service.GetCollectionCards([lotId]).Single();

        // Reassign to a different card (identity change -> moves the lot to a different Product)
        card.GameCardId = "new-id";
        card.Name = "Correct Card";
        card.SetName = "Beta";
        card.SetCode = "leb";
        card.Number = "2";
        card.Rarity = "rare";
        card.ImageUri = "https://img/correct.jpg";
        service.UpdateCollectionCard(card);

        using (var ctx = new OmniCardDbContext(_omniOptions))
        {
            var updatedLot = ctx.Lots.AsNoTracking().Include(l => l.Product).Single(l => l.Id == lotId);
            Assert.Equal("new-id", updatedLot.Product.GameCardId);
            Assert.Equal("Correct Card", updatedLot.Product.Name);
            Assert.Equal("Beta", updatedLot.Product.SetName);

            // The old product row is untouched (still exists, still "Wrong Card")
            var oldProduct = ctx.Products.AsNoTracking().Single(p => p.GameCardId == "old-id");
            Assert.Equal("Wrong Card", oldProduct.Name);
        }
    }

    [Fact]
    public void DeleteCollectionCard_RemovesFromDb()
    {
        int lotId, productId;
        using (var ctx = new OmniCardDbContext(_omniOptions))
        {
            var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = "id1", Name = "Test Card" };
            ctx.Products.Add(product);
            ctx.SaveChanges();
            productId = product.Id;

            var lot = new InventoryLot { ProductId = product.Id };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();
            lotId = lot.Id;
        }

        var service = CreateService();
        service.DeleteCollectionCard(lotId);

        using (var ctx = new OmniCardDbContext(_omniOptions))
        {
            Assert.Empty(ctx.Lots.ToList());
            // The Product catalog row is left in place.
            Assert.NotNull(ctx.Products.Find(productId));
        }
    }

    [Fact]
    public void DeleteCollectionCard_NonExistentId_DoesNotThrow()
    {
        var service = CreateService();
        service.DeleteCollectionCard(99999);
        // Should not throw
    }

    [Fact]
    public void CommitScans_MissingFromDatabase_CommitsAsUnknownCard()
    {
        var service = CreateService();

        var scan = new ScannedCard
        {
            TempImagePath = Path.GetTempFileName(),
            Hash = 0,
            Game = CardGame.Mtg,
            Match = null,
            FlagReason = FlagReason.MissingFromDatabase,
            Condition = "NM",
            IsFoil = false,
        };

        try
        {
            service.CommitScans([scan]);

            using var ctx = new OmniCardDbContext(_omniOptions);
            var lot = ctx.Lots.AsNoTracking().Include(l => l.Product).Single();
            Assert.Equal("Unknown Card", lot.Product.Name);
            Assert.True(lot.IsMissing);
            Assert.Equal(CardGame.Mtg, lot.Product.Game);
            Assert.Equal("", lot.Product.GameCardId);
        }
        finally
        {
            File.Delete(scan.TempImagePath);
        }
    }

    [Fact]
    public void CommitScans_NoMatchWithoutMissingFlag_IsSkipped()
    {
        var service = CreateService();

        var scan = new ScannedCard
        {
            TempImagePath = Path.GetTempFileName(),
            Hash = 0,
            Game = CardGame.Mtg,
            Match = null,
            FlagReason = FlagReason.NoMatch,
            Condition = "NM",
        };

        try
        {
            service.CommitScans([scan]);

            using var ctx = new OmniCardDbContext(_omniOptions);
            Assert.Empty(ctx.Lots.ToList());
        }
        finally
        {
            File.Delete(scan.TempImagePath);
        }
    }

    private CardService CreateService()
    {
        return new CardService(
            new StubHashService(),
            [],
            new MockCollectionDbContextFactory(_options),
            new MockOmniDbContextFactory(_omniOptions),
            new StubOcrService(),
            new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
            NullLogger<CardService>.Instance,
            new DataPathService(Path.GetTempPath()),
            new NullScanDiagnosticService(),
            new NullAuditService());
    }

    private class StubHashService : IPerceptualHashService
    {
        public ulong ComputeHash(System.IO.Stream imageStream, Action<HashStageResult>? onStage = null) => 0;
        public ulong ComputeEdgeHash(System.IO.Stream imageStream, Action<HashStageResult>? onStage = null) => 0;
        public ulong[] ComputeArtHash(System.IO.Stream imageStream, (double X, double Y, double W, double H)[] cropRegions, Action<HashStageResult>? onStage = null) => new ulong[cropRegions.Length];
    }

    private class StubOcrService : IOcrMatchingService
    {
        public Dictionary<string, ulong> SymbolHashes { get; set; } = [];
        public Task<OcrMatchResult> AnalyzeCardAsync(byte[] imageData) => Task.FromResult(new OcrMatchResult());
        public (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] imageData) => ([], 0);
        public Task<(string? CollectorNumber, double Confidence)> DetectOptcgCollectorNumberAsync(byte[] imageData) => Task.FromResult<(string?, double)>((null, 0));
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

    private class MockOmniDbContextFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
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
