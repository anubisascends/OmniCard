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

/// <summary>
/// Phase 2a Task 4 safety net: proves the CardService write facade — now translating
/// CollectionCard writes onto Product/InventoryLot in OmniCardDbContext instead of the
/// Phase-1 CollectionDbContext.Cards table — produces the right Products/Lots/Movements,
/// and that ids returned/consumed everywhere are LotIds.
/// </summary>
public class FacadeWriteTests : IDisposable
{
    private readonly SqliteConnection _omniConnection;
    private readonly DbContextOptions<OmniCardDbContext> _omniOptions;

    public FacadeWriteTests()
    {
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
        _omniConnection.Dispose();
    }

    private CardService CreateService() => new(
        new StubHashService(),
        [],
        new MockOmniDbContextFactory(_omniOptions),
        new StubOcrService(),
        new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
        NullLogger<CardService>.Instance,
        new DataPathService(Path.GetTempPath()),
        new NullScanDiagnosticService(),
        new NullAuditService());

    private static CardMatch MakeMatch(string gameCardId, string name, string setCode = "lea", string setName = "Alpha",
        string number = "1", string rarity = "common", string? imageUri = "https://img/card.jpg") => new()
    {
        Name = name,
        SetCode = setCode,
        SetName = setName,
        CollectorNumber = number,
        Rarity = rarity,
        ImageUri = imageUri,
        GameSpecificId = gameCardId,
    };

    private static ScannedCard MakeScan(CardMatch? match, CardGame game = CardGame.Mtg, bool isFoil = false,
        decimal? price = null, string condition = "NM", FlagReason flagReason = FlagReason.None,
        StorageContainer? overrideContainer = null) => new()
    {
        TempImagePath = Path.GetTempFileName(),
        Hash = 0,
        Game = game,
        Match = match,
        IsFoil = isFoil,
        PurchasePrice = price,
        Condition = condition,
        FlagReason = flagReason,
        OverrideContainer = overrideContainer,
    };

    // --- CommitScans: Product dedup + Lot + Acquire movement ---

    [Fact]
    public void CommitScans_MatchedCard_CreatesProductLotAndAcquireMovement()
    {
        var service = CreateService();
        var scan = MakeScan(MakeMatch("bolt-1", "Lightning Bolt"));

        try
        {
            service.CommitScans([scan]);

            using var ctx = new OmniCardDbContext(_omniOptions);
            var lot = ctx.Lots.AsNoTracking().Include(l => l.Product).Single();
            Assert.Equal("Lightning Bolt", lot.Product.Name);
            Assert.Equal(ProductCategory.Single, lot.Product.Category);
            Assert.Equal(1, lot.Quantity);

            var movement = ctx.Movements.AsNoTracking().Single();
            Assert.Equal(MovementType.Acquire, movement.Type);
            Assert.Equal(lot.Id, movement.LotId);
            Assert.Equal(lot.ProductId, movement.ProductId);
            Assert.Equal(1, movement.Quantity);
        }
        finally
        {
            File.Delete(scan.TempImagePath);
        }
    }

    [Fact]
    public void CommitScans_SamePrintingTwice_DedupsProductButCreatesTwoLots()
    {
        var service = CreateService();
        var scan1 = MakeScan(MakeMatch("bolt-1", "Lightning Bolt"));
        var scan2 = MakeScan(MakeMatch("bolt-1", "Lightning Bolt"));

        try
        {
            service.CommitScans([scan1, scan2]);

            using var ctx = new OmniCardDbContext(_omniOptions);
            Assert.Single(ctx.Products.AsNoTracking().ToList());
            Assert.Equal(2, ctx.Lots.AsNoTracking().ToList().Count);
            Assert.Equal(2, ctx.Movements.AsNoTracking().Count(m => m.Type == MovementType.Acquire));
        }
        finally
        {
            File.Delete(scan1.TempImagePath);
            File.Delete(scan2.TempImagePath);
        }
    }

    [Fact]
    public void CommitScans_SameGameCardIdDifferentFoil_CreatesSeparateProducts()
    {
        var service = CreateService();
        var nonFoil = MakeScan(MakeMatch("bolt-1", "Lightning Bolt"), isFoil: false);
        var foil = MakeScan(MakeMatch("bolt-1", "Lightning Bolt"), isFoil: true);

        try
        {
            service.CommitScans([nonFoil, foil]);

            using var ctx = new OmniCardDbContext(_omniOptions);
            var products = ctx.Products.AsNoTracking().ToList();
            Assert.Equal(2, products.Count);
            Assert.Contains(products, p => p.Foil);
            Assert.Contains(products, p => !p.Foil);
        }
        finally
        {
            File.Delete(nonFoil.TempImagePath);
            File.Delete(foil.TempImagePath);
        }
    }

    // --- AddCardToCollection: Product dedup + qty Lots + Acquire movements ---

    [Fact]
    public void AddCardToCollection_QuantityThree_CreatesOneProductAndThreeLots()
    {
        var service = CreateService();
        var match = MakeMatch("bolt-1", "Lightning Bolt");

        service.AddCardToCollection(match, CardGame.Mtg, "NM", false, 1.50m, 3, null, null, null, null);

        using var ctx = new OmniCardDbContext(_omniOptions);
        Assert.Single(ctx.Products.AsNoTracking().ToList());
        var lots = ctx.Lots.AsNoTracking().ToList();
        Assert.Equal(3, lots.Count);
        Assert.All(lots, l => Assert.Equal(1, l.Quantity));
        Assert.All(lots, l => Assert.Equal(1.50m, l.UnitCost));
        Assert.Equal(3, ctx.Movements.AsNoTracking().Count(m => m.Type == MovementType.Acquire));
    }

    [Fact]
    public void AddCardToCollection_ExistingProduct_ReusesIt()
    {
        var service = CreateService();
        var match = MakeMatch("bolt-1", "Lightning Bolt");

        service.AddCardToCollection(match, CardGame.Mtg, "NM", false, null, 1, null, null, null, null);
        service.AddCardToCollection(match, CardGame.Mtg, "LP", false, null, 1, null, null, null, null);

        using var ctx = new OmniCardDbContext(_omniOptions);
        Assert.Single(ctx.Products.AsNoTracking().ToList());
        Assert.Equal(2, ctx.Lots.AsNoTracking().ToList().Count);
    }

    [Fact]
    public void AddCardToCollection_BinderContainer_SetsPageAndSlot()
    {
        var service = CreateService();
        int containerId;
        using (var seedCtx = new OmniCardDbContext(_omniOptions))
        {
            var container = new StorageContainer { Name = "Binder", ContainerType = ContainerType.Binder };
            seedCtx.StorageContainers.Add(container);
            seedCtx.SaveChanges();
            containerId = container.Id;
        }

        var match = MakeMatch("bolt-1", "Lightning Bolt");
        service.AddCardToCollection(match, CardGame.Mtg, "NM", false, null, 1,
            new StorageContainer { Id = containerId, ContainerType = ContainerType.Binder }, page: 2, slot: 5, section: null);

        using var ctx = new OmniCardDbContext(_omniOptions);
        var lot = ctx.Lots.AsNoTracking().Single();
        Assert.Equal(containerId, lot.LocationId);
        Assert.Equal(2, lot.Page);
        Assert.Equal(5, lot.Slot);
    }

    // --- UpdateCollectionCard: copy-attr update + identity-change lot move ---

    [Fact]
    public void UpdateCollectionCard_CopyAttrsOnly_UpdatesLotWithoutTouchingProduct()
    {
        var service = CreateService();
        int lotId, productId;
        using (var seedCtx = new OmniCardDbContext(_omniOptions))
        {
            var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = "bolt-1", Name = "Lightning Bolt", Foil = false };
            seedCtx.Products.Add(product);
            seedCtx.SaveChanges();
            productId = product.Id;

            var seedLot = new InventoryLot { ProductId = product.Id, Condition = "NM" };
            seedCtx.Lots.Add(seedLot);
            seedCtx.SaveChanges();
            lotId = seedLot.Id;
        }

        var card = service.GetCollectionCards([lotId]).Single();
        card.Condition = "HP";
        card.PurchasePrice = 9.99m;
        service.UpdateCollectionCard(card);

        using var ctx = new OmniCardDbContext(_omniOptions);
        Assert.Single(ctx.Products.AsNoTracking().ToList());
        var lot = ctx.Lots.AsNoTracking().Single();
        Assert.Equal(productId, lot.ProductId);
        Assert.Equal("HP", lot.Condition);
        Assert.Equal(9.99m, lot.UnitCost);
    }

    [Fact]
    public void UpdateCollectionCard_FoilFlip_MovesLotToDifferentProduct()
    {
        var service = CreateService();
        int lotId, originalProductId;
        using (var seedCtx = new OmniCardDbContext(_omniOptions))
        {
            var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = "bolt-1", Name = "Lightning Bolt", Foil = false };
            seedCtx.Products.Add(product);
            seedCtx.SaveChanges();
            originalProductId = product.Id;

            var seedLot = new InventoryLot { ProductId = product.Id, Condition = "NM" };
            seedCtx.Lots.Add(seedLot);
            seedCtx.SaveChanges();
            lotId = seedLot.Id;
        }

        var card = service.GetCollectionCards([lotId]).Single();
        Assert.False(card.IsFoil);
        card.IsFoil = true; // identity change: same printing, foil flip
        service.UpdateCollectionCard(card);

        using var ctx = new OmniCardDbContext(_omniOptions);
        var products = ctx.Products.AsNoTracking().ToList();
        Assert.Equal(2, products.Count); // original non-foil Product untouched + new foil Product

        var lot = ctx.Lots.AsNoTracking().Include(l => l.Product).Single(l => l.Id == lotId);
        Assert.NotEqual(originalProductId, lot.ProductId);
        Assert.True(lot.Product.Foil);
        Assert.Equal("Lightning Bolt", lot.Product.Name);

        var originalProduct = products.Single(p => p.Id == originalProductId);
        Assert.False(originalProduct.Foil);
    }

    [Fact]
    public void UpdateCollectionCard_IdentityChangeToExistingProduct_ReusesIt()
    {
        var service = CreateService();
        int lotId;
        int targetProductId;
        using (var seedCtx = new OmniCardDbContext(_omniOptions))
        {
            var wrongProduct = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = "wrong-1", Name = "Wrong Card" };
            var correctProduct = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = "correct-1", Name = "Correct Card" };
            seedCtx.Products.AddRange(wrongProduct, correctProduct);
            seedCtx.SaveChanges();
            targetProductId = correctProduct.Id;

            var seedLot = new InventoryLot { ProductId = wrongProduct.Id };
            seedCtx.Lots.Add(seedLot);
            seedCtx.SaveChanges();
            lotId = seedLot.Id;
        }

        var card = service.GetCollectionCards([lotId]).Single();
        card.GameCardId = "correct-1";
        card.Name = "Correct Card";
        service.UpdateCollectionCard(card);

        using var ctx = new OmniCardDbContext(_omniOptions);
        // Still only 2 products -- the lot moved to the existing "Correct Card" product instead of creating a third.
        Assert.Equal(2, ctx.Products.AsNoTracking().ToList().Count);
        var lot = ctx.Lots.AsNoTracking().Single(l => l.Id == lotId);
        Assert.Equal(targetProductId, lot.ProductId);
    }

    // --- DeleteCollectionCard: removes the lot only ---

    [Fact]
    public void DeleteCollectionCard_RemovesLotButLeavesProductCatalogRow()
    {
        var service = CreateService();
        int lotId, productId;
        using (var seedCtx = new OmniCardDbContext(_omniOptions))
        {
            var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = "bolt-1", Name = "Lightning Bolt" };
            seedCtx.Products.Add(product);
            seedCtx.SaveChanges();
            productId = product.Id;

            var lot1 = new InventoryLot { ProductId = product.Id };
            var lot2 = new InventoryLot { ProductId = product.Id };
            seedCtx.Lots.AddRange(lot1, lot2);
            seedCtx.SaveChanges();
            lotId = lot1.Id;
        }

        service.DeleteCollectionCard(lotId);

        using var ctx = new OmniCardDbContext(_omniOptions);
        var remainingLots = ctx.Lots.AsNoTracking().ToList();
        Assert.Single(remainingLots); // only the second lot remains
        Assert.NotEqual(lotId, remainingLots[0].Id);
        Assert.NotNull(ctx.Products.Find(productId)); // product survives
    }

    // --- BulkUpdateField: diff back onto Lot/Product ---

    [Fact]
    public void BulkUpdateField_Condition_UpdatesAllLots()
    {
        var service = CreateService();
        var ids = SeedLots(3);

        service.BulkUpdateField(ids, c => c.Condition = "LP");

        using var ctx = new OmniCardDbContext(_omniOptions);
        Assert.All(ctx.Lots.AsNoTracking().ToList(), l => Assert.Equal("LP", l.Condition));
    }

    [Fact]
    public void BulkUpdateField_Foil_MovesEachLotToSharedNewProduct()
    {
        var service = CreateService();
        var ids = SeedLots(2);

        service.BulkUpdateField(ids, c => c.IsFoil = true);

        using var ctx = new OmniCardDbContext(_omniOptions);
        var lots = ctx.Lots.AsNoTracking().Include(l => l.Product).Where(l => ids.Contains(l.Id)).ToList();
        Assert.All(lots, l => Assert.True(l.Product.Foil));
        // Both lots reassigned to the SAME new foil product (deduped within the batch), not two separate ones.
        Assert.Equal(1, lots.Select(l => l.ProductId).Distinct().Count());
    }

    [Fact]
    public void BulkUpdateField_PriceAndLocation_UpdatesLots()
    {
        var service = CreateService();
        var ids = SeedLots(2);
        int containerId;
        using (var seedCtx = new OmniCardDbContext(_omniOptions))
        {
            var container = new StorageContainer { Name = "Box A", ContainerType = ContainerType.Box };
            seedCtx.StorageContainers.Add(container);
            seedCtx.SaveChanges();
            containerId = container.Id;
        }

        service.BulkUpdateField(ids, c =>
        {
            c.PurchasePrice = 3.25m;
            c.ContainerId = containerId;
        });

        using var ctx = new OmniCardDbContext(_omniOptions);
        var lots = ctx.Lots.AsNoTracking().Where(l => ids.Contains(l.Id)).ToList();
        Assert.All(lots, l => Assert.Equal(3.25m, l.UnitCost));
        Assert.All(lots, l => Assert.Equal(containerId, l.LocationId));
    }

    // --- MoveCardsToContainer: LocationId (+ Move movement) ---

    [Fact]
    public void MoveCardsToContainer_UpdatesLocationIdAndClearsPageSlot()
    {
        var service = CreateService();
        var ids = SeedLots(2);
        int containerId;
        using (var seedCtx = new OmniCardDbContext(_omniOptions))
        {
            var container = new StorageContainer { Name = "Box B", ContainerType = ContainerType.Box };
            seedCtx.StorageContainers.Add(container);
            seedCtx.SaveChanges();
            containerId = container.Id;

            // Give the lots a stale page/slot from a previous binder placement.
            foreach (var lot in seedCtx.Lots.Where(l => ids.Contains(l.Id)))
            {
                lot.Page = 3;
                lot.Slot = 7;
            }
            seedCtx.SaveChanges();
        }

        service.MoveCardsToContainer(ids, containerId, "Section A");

        using var ctx = new OmniCardDbContext(_omniOptions);
        var lots = ctx.Lots.AsNoTracking().Where(l => ids.Contains(l.Id)).ToList();
        Assert.All(lots, l => Assert.Equal(containerId, l.LocationId));
        Assert.All(lots, l => Assert.Null(l.Page));
        Assert.All(lots, l => Assert.Null(l.Slot));
        Assert.All(lots, l => Assert.Equal("Section A", l.Section));

        var moves = ctx.Movements.AsNoTracking().Where(m => m.Type == MovementType.Move).ToList();
        Assert.Equal(2, moves.Count);
    }

    // --- Helpers ---

    private List<int> SeedLots(int count)
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = "bolt-1", Name = "Lightning Bolt", Foil = false };
        ctx.Products.Add(product);
        ctx.SaveChanges();

        var ids = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var lot = new InventoryLot { ProductId = product.Id, Condition = "NM" };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();
            ids.Add(lot.Id);
        }
        return ids;
    }

    private class StubHashService : IPerceptualHashService
    {
        public ulong ComputeHash(Stream imageStream, Action<HashStageResult>? onStage = null) => 0;
        public ulong ComputeEdgeHash(Stream imageStream, Action<HashStageResult>? onStage = null) => 0;
        public ulong[] ComputeArtHash(Stream imageStream, (double X, double Y, double W, double H)[] cropRegions, Action<HashStageResult>? onStage = null) => new ulong[cropRegions.Length];
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

    private class MockOmniDbContextFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
