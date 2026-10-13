using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Api.Contracts;
using OmniCard.Collection.Inventory;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Settings;
using OmniCard.Shared.Sites;
using OmniCard.Shared.Storage;
using OmniCard.Tests.Fakes;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>Covers deck box ↔ deck URL linking and "Sync from URL": the by-name diff, suggested copies
/// (exact printing first, ignored locations and listed copies never suggested, read-only sites hidden),
/// the "not in collection" list, and applying the user's choices.</summary>
public class DeckBoxSyncServiceTests : IDisposable
{
    private const string DeckUrl = "https://moxfield.com/decks/abc";

    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly DeckBoxSyncService _service;
    private readonly StubDecklists _decklists = new();
    private readonly StorageContainerService _containers;
    private readonly TagService _tags;
    private readonly int _deckBoxId;
    private readonly int _bulkId;
    private readonly int _binderId;

    public DeckBoxSyncServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();
        var factory = new Factory(_opts);

        _containers = new StorageContainerService(factory);
        _tags = new TagService(factory);
        _service = new DeckBoxSyncService(factory, _decklists, new RecordingCardService(new ConfigurableGameService()),
            new WebBinderCardService(factory, new StubDataPath()), _tags, NullLogger<DeckBoxSyncService>.Instance);

        _deckBoxId = _containers.Create("Commander", ContainerType.DeckBox, game: CardGame.Mtg).Id;
        _bulkId = _containers.Create("Spare Bulk", ContainerType.Box).Id;
        _binderId = _containers.Create("Trade Binder", ContainerType.Binder).Id;
        _service.SetLink(_deckBoxId, DeckUrl);
    }

    public void Dispose() => _conn.Dispose();

    private int Lot(string name, int locationId, int quantity = 1, string set = "SET", string cn = "1", bool foil = false,
        CardGame game = CardGame.Mtg)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var product = new Product
        {
            Game = game, Category = ProductCategory.Single, Name = name, SetCode = set, CollectorNumber = cn, Foil = foil,
            GameCardId = $"{name}-{set}-{cn}-{foil}".Replace(" ", ""),
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = product.Id, LocationId = locationId, Quantity = quantity, Condition = "NM" };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    private void Deck(params DecklistEntry[] entries) => _decklists.FetchResult = ("Test Deck", entries.ToList());

    private List<(int LotId, string Name, int Qty, int? LocationId)> Lots()
    {
        using var ctx = new OmniCardDbContext(_opts);
        return ctx.Lots.Include(l => l.Product).AsEnumerable()
            .Select(l => (l.Id, l.Product.Name, l.Quantity, l.LocationId)).ToList();
    }

    private int InBox(string name) => Lots().Where(l => l.Name == name && l.LocationId == _deckBoxId).Sum(l => l.Qty);

    [Fact]
    public async Task Preview_DiffsByName_AndSuggestsExactPrintingFirst()
    {
        Lot("Sol Ring", _deckBoxId);
        Lot("Counterspell", _deckBoxId, quantity: 2);
        Lot("Lightning Bolt", _deckBoxId, set: "M10");
        var otherBolt = Lot("Lightning Bolt", _binderId, quantity: 3, set: "M10");
        var exactBolt = Lot("Lightning Bolt", _bulkId, set: "2X2", cn: "117");
        Deck(new DecklistEntry(1, "Sol Ring", "SCD", "276"),
            new DecklistEntry(4, "Lightning Bolt", "2X2", "117"),
            new DecklistEntry(1, "Black Lotus", "LEA", "232"));

        var plan = await _service.PreviewAsync(_deckBoxId, SiteAccess.Unrestricted);

        Assert.Equal("Test Deck", plan.DeckName);
        Assert.Equal(6, plan.DeckCardCount);
        Assert.Equal(2, plan.KeepCount); // Sol Ring (any printing) + the boxed Bolt

        var bolt = Assert.Single(plan.Adds, a => a.CardName == "Lightning Bolt");
        Assert.Equal(3, bolt.Needed);
        Assert.Equal([exactBolt, otherBolt], bolt.Sources.Select(s => s.LotId));
        Assert.True(bolt.Sources[0].IsExactPrinting);
        Assert.Equal([1, 2], bolt.Sources.Select(s => s.Suggested));

        var lotus = Assert.Single(plan.Adds, a => a.CardName == "Black Lotus");
        Assert.Empty(lotus.Sources);

        var cut = Assert.Single(plan.Cuts);
        Assert.Equal(("Counterspell", 2), (cut.CardName, cut.Quantity));
    }

    [Fact]
    public async Task Preview_NeverSuggestsListedOrIgnoredCopies_AndHidesReadOnlySites()
    {
        var listed = Lot("Sol Ring", _bulkId);
        var ignoredBox = _containers.Create("Other Deck", ContainerType.DeckBox, game: CardGame.Mtg).Id;
        _containers.SetExcludeFromDeckCheck(ignoredBox, true);
        var ignored = Lot("Sol Ring", ignoredBox);
        int shopId;
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Listings.Add(new Listing { LotId = listed, Status = ListingStatus.Listed, ListedPrice = 1m });
            var shop = new Site { Name = "Shop" };
            ctx.Sites.Add(shop);
            ctx.SaveChanges();
            shopId = shop.Id;
        }
        var shopBinder = _containers.Create("Shop Binder", ContainerType.Binder, siteId: shopId).Id;
        Lot("Sol Ring", shopBinder);
        Deck(new DecklistEntry(1, "Sol Ring", null, null));

        var access = SiteAccess.FromGrants(new Dictionary<int, SiteAccessLevel> { [shopId] = SiteAccessLevel.Read });
        var add = Assert.Single((await _service.PreviewAsync(_deckBoxId, access)).Adds);

        Assert.Equal([(ignored, "Location", 0), (listed, "Listed", 0)],
            add.Sources.Select(s => (s.LotId, s.IgnoredReason!, s.Suggested)));
    }

    [Fact]
    public async Task Preview_KeepsSideboardTaggedCutsFlagged_AndFlagsPreviouslyMarkedCards()
    {
        var side = Lot("Pyroblast", _deckBoxId);
        _tags.AddTagToLots([side], DeckBoxSyncService.SideboardTag);
        Deck(new DecklistEntry(1, "Black Lotus", null, null));
        _service.Apply(_deckBoxId, new DeckSyncApplyRequest
        {
            Cuts = [new DeckSyncCutDecisionDto(side, 1, "Keep", null)],
            NotInCollection = [new DeckBoxMissingCardDto("Black Lotus", null, null, 1)],
        }, SiteAccess.Unrestricted);

        var plan = await _service.PreviewAsync(_deckBoxId, SiteAccess.Unrestricted);

        Assert.True(Assert.Single(plan.Cuts).IsSideboard);
        Assert.True(Assert.Single(plan.Adds).MarkedNotInCollection);
    }

    [Fact]
    public void Apply_MovesCopiesIn_MovesOrSideboardsCuts_AndReplacesNotInCollection()
    {
        var bolts = Lot("Lightning Bolt", _binderId, quantity: 3);
        var counter = Lot("Counterspell", _deckBoxId);
        var brainstorms = Lot("Brainstorm", _deckBoxId, quantity: 3);
        _service.Apply(_deckBoxId, new DeckSyncApplyRequest
        {
            NotInCollection = [new DeckBoxMissingCardDto("Old Card", null, null, 1)],
        }, SiteAccess.Unrestricted);

        var result = _service.Apply(_deckBoxId, new DeckSyncApplyRequest
        {
            Picks = [new DeckSyncPickDto(bolts, 2)],
            Cuts =
            [
                new DeckSyncCutDecisionDto(counter, 1, "Move", _bulkId),
                new DeckSyncCutDecisionDto(brainstorms, 1, "Sideboard", null),
            ],
            NotInCollection = [new DeckBoxMissingCardDto("Black Lotus", "LEA", "232", 1)],
        }, SiteAccess.Unrestricted);

        Assert.Equal(new DeckSyncResultDto(2, 1, 1, 1), result);
        Assert.Equal(2, InBox("Lightning Bolt"));
        Assert.Equal(1, Lots().Single(l => l.LotId == bolts).Qty); // the rest stayed in the binder
        Assert.Equal(_bulkId, Lots().Single(l => l.LotId == counter).LocationId);
        Assert.Equal(3, InBox("Brainstorm"));
        var tagged = _tags.GetTagsByLots(Lots().Where(l => l.Name == "Brainstorm").Select(l => l.LotId))
            .Where(kv => kv.Value.Contains(DeckBoxSyncService.SideboardTag)).Select(kv => kv.Key).ToList();
        Assert.Equal(1, Lots().Single(l => l.LotId == Assert.Single(tagged)).Qty);

        var missing = Assert.Single(_service.GetNotInCollection(_deckBoxId));
        Assert.Equal(new DeckBoxMissingCardDto("Black Lotus", "LEA", "232", 1), missing);
        using var ctx = new OmniCardDbContext(_opts);
        Assert.NotNull(ctx.StorageContainers.Find(_deckBoxId)!.SourceSyncedAt);
    }

    [Fact]
    public void Apply_RejectsListedCopies_CopiesAlreadyInTheBox_AndReadOnlySites_WithoutWriting()
    {
        var listed = Lot("Sol Ring", _bulkId);
        var boxed = Lot("Mana Crypt", _deckBoxId);
        var fine = Lot("Lightning Bolt", _binderId);
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Listings.Add(new Listing { LotId = listed, Status = ListingStatus.Listed, ListedPrice = 1m });
            ctx.SaveChanges();
        }

        DeckSyncApplyRequest Pick(int lotId) => new() { Picks = [new DeckSyncPickDto(fine, 1), new DeckSyncPickDto(lotId, 1)] };
        Assert.Throws<DeckBoxSyncService.DeckSyncException>(() => _service.Apply(_deckBoxId, Pick(listed), SiteAccess.Unrestricted));
        Assert.Throws<DeckBoxSyncService.DeckSyncException>(() => _service.Apply(_deckBoxId, Pick(boxed), SiteAccess.Unrestricted));
        Assert.Throws<DeckBoxSyncService.DeckSyncException>(() => _service.Apply(_deckBoxId,
            new DeckSyncApplyRequest { Cuts = [new DeckSyncCutDecisionDto(boxed, 1, "Move", null)] }, SiteAccess.Unrestricted));

        Assert.Equal(0, InBox("Lightning Bolt"));
        Assert.Equal(_deckBoxId, Lots().Single(l => l.LotId == boxed).LocationId);
    }

    [Fact]
    public void SetLink_ValidatesSiteAndGame_AndChangingItClearsNotInCollection()
    {
        var pokemonBox = _containers.Create("Pikachu", ContainerType.DeckBox, game: CardGame.Pokemon).Id;
        Assert.Throws<DeckBoxSyncService.DeckSyncException>(() => _service.SetLink(pokemonBox, DeckUrl));
        Assert.Throws<DeckBoxSyncService.DeckSyncException>(() => _service.SetLink(_deckBoxId, "https://example.com/decks/1"));
        Assert.Throws<DeckBoxSyncService.DeckSyncException>(() => _service.SetLink(_bulkId, DeckUrl));

        _service.Apply(_deckBoxId, new DeckSyncApplyRequest
        {
            NotInCollection = [new DeckBoxMissingCardDto("Black Lotus", null, null, 1)],
        }, SiteAccess.Unrestricted);
        _service.SetLink(_deckBoxId, DeckUrl); // unchanged: keeps the list
        Assert.Single(_service.GetNotInCollection(_deckBoxId));

        _service.SetLink(_deckBoxId, "https://archidekt.com/decks/42");
        Assert.Empty(_service.GetNotInCollection(_deckBoxId));

        _service.SetLink(_deckBoxId, "  ");
        using var ctx = new OmniCardDbContext(_opts);
        Assert.Null(ctx.StorageContainers.Find(_deckBoxId)!.SourceUrl);
    }

    [Fact]
    public void Search_FindsOwnedCopiesByName_OutsideTheBox_OfTheBoxGame()
    {
        var bolt = Lot("Lightning Bolt", _binderId);
        Lot("Lightning Bolt", _deckBoxId);
        Lot("Lightning Dragon", _bulkId, game: CardGame.Pokemon);

        var found = _service.Search(_deckBoxId, "bolt", SiteAccess.Unrestricted);

        Assert.Equal(bolt, Assert.Single(found).LotId);
        Assert.Empty(_service.Search(_deckBoxId, "b", SiteAccess.Unrestricted));
    }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    private sealed class StubDecklists : IDecklistService
    {
        public (string DeckName, List<DecklistEntry> Entries)? FetchResult { get; set; }
        public Task<(string DeckName, List<DecklistEntry> Entries)?> FetchDecklistAsync(string url) => Task.FromResult(FetchResult);
        public (string DeckName, List<DecklistEntry> Entries) ParseDecklistText(string text) => throw new NotImplementedException();
        public List<DecklistEntry> ParseDecklistPrintings(string text) => throw new NotImplementedException();
        public DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries, CardGame game, IReadOnlyCollection<int>? siteIds = null) => throw new NotImplementedException();
    }

    private sealed class StubDataPath : IDataPathService
    {
        public string DataDirectory => Path.GetTempPath();
        public string ScansDirectory => Path.GetTempPath();
        public string TempScansDirectory => Path.GetTempPath();
        public string SymbolsCacheDirectory => Path.GetTempPath();
        public string LogsDirectory => Path.GetTempPath();
        public string TradesDirectory => Path.GetTempPath();
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
