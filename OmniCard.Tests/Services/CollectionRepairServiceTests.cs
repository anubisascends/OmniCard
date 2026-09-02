using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class CollectionRepairServiceTests : IDisposable
{
    private readonly SqliteConnection _omniConn;
    private readonly DbContextOptions<OmniCardDbContext> _omniOptions;
    private readonly SqliteConnection _scryConn;
    private readonly ScryfallDbContext _scryContext;

    public CollectionRepairServiceTests()
    {
        _omniConn = new SqliteConnection("Data Source=:memory:");
        _omniConn.Open();
        _omniOptions = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_omniConn).Options;
        using (var ctx = new OmniCardDbContext(_omniOptions)) ctx.Database.EnsureCreated();

        _scryConn = new SqliteConnection("Data Source=:memory:");
        _scryConn.Open();
        var scryOptions = new DbContextOptionsBuilder<ScryfallDbContext>().UseSqlite(_scryConn).Options;
        _scryContext = new ScryfallDbContext(scryOptions);
        _scryContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _scryContext.Dispose();
        _scryConn.Dispose();
        _omniConn.Dispose();
    }

    private CollectionRepairService CreateService() =>
        new(new MockOmniDbContextFactory(_omniOptions), new FakeScryfall(_scryContext),
            NullLogger<CollectionRepairService>.Instance);

    private void SeedScryfall(params Card[] cards)
    {
        _scryContext.Cards.AddRange(cards);
        _scryContext.SaveChanges();
    }

    private static Card HobCard(string number, List<string>? colors, string typeLine, string name = "X") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Lang = "en",
        SetCode = "hob",
        SetName = "The Hobbit",
        CollectorNumber = number,
        Colors = colors,
        ColorIdentity = colors ?? [],
        TypeLine = typeLine,
    };

    private Product SeedProduct(string setCode, string number, bool foil, string? color, string? gameCardId = null)
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        var p = new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Single,
            GameCardId = gameCardId ?? Guid.NewGuid().ToString(),
            Name = "Card " + number,
            SetCode = setCode,
            CollectorNumber = number,
            Foil = foil,
            Color = color,
        };
        ctx.Products.Add(p);
        ctx.SaveChanges();
        ctx.Lots.Add(new InventoryLot { ProductId = p.Id });
        ctx.SaveChanges();
        return p;
    }

    [Fact]
    public void RepairIfNeeded_BackfillsNullColor_AndLowercasesSetCode()
    {
        SeedScryfall(HobCard("10", ["W"], "Artifact — Equipment", "Dwarven Shortsword"));
        var product = SeedProduct("HOB", "10", foil: true, color: null);

        var changed = CreateService().RepairIfNeeded();

        Assert.True(changed >= 1);
        using var ctx = new OmniCardDbContext(_omniOptions);
        var repaired = ctx.Products.Single(p => p.Id == product.Id);
        Assert.Equal("W", repaired.Color);
        Assert.Equal("Artifact", repaired.CardType);
        Assert.Equal("hob", repaired.SetCode); // authoritative lowercase
        Assert.Equal("The Hobbit", repaired.SetName);
    }

    [Fact]
    public void RepairIfNeeded_DoesNotOverwriteExistingColor()
    {
        SeedScryfall(HobCard("10", ["W"], "Artifact — Equipment"));
        var product = SeedProduct("hob", "10", foil: false, color: "CUSTOM");

        CreateService().RepairIfNeeded();

        using var ctx = new OmniCardDbContext(_omniOptions);
        Assert.Equal("CUSTOM", ctx.Products.Single(p => p.Id == product.Id).Color);
    }

    [Fact]
    public void RepairIfNeeded_ColorlessArtifact_MapsToColorless_LandsToLand()
    {
        SeedScryfall(
            HobCard("133", [], "Creature — Bear"),          // no colors, not land
            HobCard("184", null, "Land"));                   // land
        var artifact = SeedProduct("HOB", "133", foil: false, color: null);
        var land = SeedProduct("HOB", "184", foil: false, color: null);

        CreateService().RepairIfNeeded();

        using var ctx = new OmniCardDbContext(_omniOptions);
        Assert.Equal("Colorless", ctx.Products.Single(p => p.Id == artifact.Id).Color);
        Assert.Equal("Land", ctx.Products.Single(p => p.Id == land.Id).Color);
    }

    [Fact]
    public void RepairIfNeeded_MergesGenuineDuplicates_ReassigningLots()
    {
        SeedScryfall(HobCard("10", ["W"], "Artifact — Equipment"));
        // Same printing (set/number/foil) but two rows with different GameCardIds — a genuine dup.
        var withColor = SeedProduct("hob", "10", foil: true, color: "W");
        var orphan = SeedProduct("HOB", "10", foil: true, color: null);

        var changed = CreateService().RepairIfNeeded();

        using var ctx = new OmniCardDbContext(_omniOptions);
        Assert.Null(ctx.Products.FirstOrDefault(p => p.Id == orphan.Id)); // orphan deleted
        var canonical = ctx.Products.Single(p => p.Id == withColor.Id);
        Assert.Equal(2, ctx.Lots.Count(l => l.ProductId == canonical.Id)); // both lots repointed
        Assert.True(changed >= 1);
    }

    [Fact]
    public void RepairIfNeeded_IsIdempotent_AndSkipsWhenCatalogEmpty()
    {
        // No scryfall cards seeded → catalog empty → skip and DON'T write the marker.
        SeedProduct("HOB", "10", foil: false, color: null);
        Assert.Equal(0, CreateService().RepairIfNeeded());
        using (var ctx = new OmniCardDbContext(_omniOptions))
            Assert.False(ctx.MigrationState.Any(m => m.Key == CollectionRepairService.MigrationStateKey));

        // Now seed the catalog: first run repairs, second run is a no-op.
        SeedScryfall(HobCard("10", ["R"], "Creature — Goblin"));
        Assert.True(CreateService().RepairIfNeeded() >= 1);
        Assert.Equal(0, CreateService().RepairIfNeeded());
        using (var ctx = new OmniCardDbContext(_omniOptions))
            Assert.True(ctx.MigrationState.Any(m => m.Key == CollectionRepairService.MigrationStateKey));
    }

    // Minimal IScryfallService whose Cards queryable is backed by a live in-memory ScryfallDbContext.
    private sealed class FakeScryfall(ScryfallDbContext ctx) : IScryfallService
    {
        public IQueryable<Card> Cards => ctx.Cards;
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 10, ulong? scanEdgeHash = null) => null;
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => [];
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
        public List<SetCatalogCard> GetSetCards(string setCode) => [];
    }

    private sealed class MockOmniDbContextFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
