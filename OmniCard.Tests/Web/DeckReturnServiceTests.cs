using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Api.Contracts;
using OmniCard.Audit.Exporters;
using OmniCard.Collection.Inventory;
using OmniCard.Collection.Lists;
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

/// <summary>Covers deck origins (recorded on every move into a deck box, kept deck → deck, cleared on the way
/// out) and deck deconstruction / "return to where it came from": back to the same pocket or stack, the
/// fallback location for unknown origins, Bulk for deleted / read-only / other-game origins and taken pockets,
/// the saved put-back report and the list saved for rebuilding.</summary>
public class DeckReturnServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly WebBinderCardService _cards;
    private readonly DeckReturnService _returns;
    private readonly DeckBoxSyncService _sync;
    private readonly StorageContainerService _containers;
    private readonly TagService _tags;
    private readonly StubDecklists _decklists = new();
    private readonly int _bulkId;
    private readonly int _deckId;
    private readonly int _otherDeckId;
    private readonly int _binderId;
    private readonly int _boxId;

    public DeckReturnServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Database.EnsureCreated();
            var bulk = new StorageContainer { Name = "Bulk", ContainerType = ContainerType.Bulk, IsSystem = true };
            ctx.StorageContainers.Add(bulk);
            ctx.SaveChanges();
            _bulkId = bulk.Id;
        }
        var factory = new Factory(_opts);
        var cardService = new RecordingCardService(new ConfigurableGameService());
        _containers = new StorageContainerService(factory);
        _tags = new TagService(factory);
        _cards = new WebBinderCardService(factory, new StubDataPath());
        _returns = new DeckReturnService(factory, new ListService(factory, cardService), NullLogger<DeckReturnService>.Instance);
        _sync = new DeckBoxSyncService(factory, _decklists, cardService, _cards, _tags, _returns, NullLogger<DeckBoxSyncService>.Instance);

        _deckId = _containers.Create("Commander", ContainerType.DeckBox, game: CardGame.Mtg).Id;
        _otherDeckId = _containers.Create("Pauper", ContainerType.DeckBox, game: CardGame.Mtg).Id;
        _binderId = _containers.Create("Trade Binder", ContainerType.Binder).Id;
        _boxId = _containers.Create("Long Box", ContainerType.Box).Id;
    }

    public void Dispose() => _conn.Dispose();

    private int Lot(string name, int locationId, int quantity = 1, int? page = null, int? slot = null, string? section = null,
        CardGame game = CardGame.Mtg)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var product = ctx.Products.FirstOrDefault(p => p.Name == name && p.Game == game);
        if (product is null)
        {
            product = new Product
            {
                Game = game, Category = ProductCategory.Single, Name = name, SetCode = "SET", CollectorNumber = "1",
                GameCardId = name.Replace(" ", "").ToLowerInvariant(),
            };
            ctx.Products.Add(product);
            ctx.SaveChanges();
        }
        var lot = new InventoryLot
        {
            ProductId = product.Id, LocationId = locationId, Quantity = quantity, Condition = "NM",
            Page = page, Slot = slot, Section = section, AcquisitionDate = new DateTime(2026, 1, 1),
        };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    private InventoryLot? Get(int lotId)
    {
        using var ctx = new OmniCardDbContext(_opts);
        return ctx.Lots.AsNoTracking().FirstOrDefault(l => l.Id == lotId);
    }

    private List<InventoryLot> LotsIn(int locationId)
    {
        using var ctx = new OmniCardDbContext(_opts);
        return ctx.Lots.AsNoTracking().Include(l => l.Product).Where(l => l.LocationId == locationId).ToList();
    }

    private DeckReturnResultDto Deconstruct(int? fallback = null, string? listName = null, SiteAccess? access = null) =>
        _returns.Apply(_deckId, new DeckReturnRequest { FallbackLocationId = fallback, SaveAsListName = listName },
            access ?? SiteAccess.Unrestricted, "tester");

    // --- origins ---

    [Fact]
    public void MovingIntoADeck_RecordsWhereTheCopyWas_AndDeckToDeckKeepsIt_AndLeavingClearsIt()
    {
        var lot = Lot("Sol Ring", _binderId, page: 3, slot: 4, section: "Front");

        _cards.MoveCardsToContainer([lot], _deckId);
        var inDeck = Get(lot)!;
        Assert.Equal((_binderId, 3, 4, "Front"), (inDeck.OriginLocationId!.Value, inDeck.OriginPage!.Value, inDeck.OriginSlot!.Value, inDeck.OriginSection));

        _cards.MoveCardsToContainer([lot], _otherDeckId);
        Assert.Equal(_binderId, Get(lot)!.OriginLocationId);
        Assert.Equal(3, Get(lot)!.OriginPage);

        _cards.MoveCardsToContainer([lot], _boxId);
        Assert.Null(Get(lot)!.OriginLocationId);
        Assert.Null(Get(lot)!.OriginPage);
    }

    [Fact]
    public void PartialMoveIntoADeck_GivesTheSplitCopiesTheStacksPosition()
    {
        var stack = Lot("Counterspell", _boxId, quantity: 4, section: "Blue");

        _cards.MoveQuantitiesToContainer([(stack, 1)], _deckId);

        var split = Assert.Single(LotsIn(_deckId));
        Assert.Equal((_boxId, "Blue"), (split.OriginLocationId!.Value, split.OriginSection));
        Assert.Null(Get(stack)!.OriginLocationId); // the remainder never left
    }

    [Fact]
    public void EditingTheLocation_RecordsTheOriginToo()
    {
        var lot = Lot("Brainstorm", _binderId, page: 1, slot: 2);
        var card = _cards.GetCollectionCards([lot]).Single();
        card.ContainerId = _deckId;
        card.Page = null;
        card.Slot = null;

        _cards.UpdateCollectionCard(card);

        Assert.Equal((_binderId, 1, 2), (Get(lot)!.OriginLocationId!.Value, Get(lot)!.OriginPage!.Value, Get(lot)!.OriginSlot!.Value));
    }

    // --- deconstruct ---

    [Fact]
    public void Deconstruct_PutsCopiesBackInTheirPocket_RejoinsStacks_AndSendsUnknownOriginsToTheFallback()
    {
        var pocket = Lot("Sol Ring", _binderId, page: 2, slot: 5);
        var stack = Lot("Counterspell", _boxId, quantity: 4, section: "Blue");
        _cards.MoveCardsToContainer([pocket], _deckId);
        _cards.MoveQuantitiesToContainer([(stack, 2)], _deckId);
        var unknown = Lot("Swords to Plowshares", _deckId);

        var result = Deconstruct(fallback: _boxId);

        Assert.Empty(LotsIn(_deckId));
        var back = Get(pocket)!;
        Assert.Equal((_binderId, 2, 5), (back.LocationId!.Value, back.Page!.Value, back.Slot!.Value));
        Assert.Null(back.OriginLocationId);
        Assert.Equal(4, Get(stack)!.Quantity); // the two copies rejoined their stack
        Assert.Single(LotsIn(_boxId), l => l.Product.Name == "Counterspell");
        Assert.Equal(_boxId, Get(unknown)!.LocationId);

        Assert.Equal(4, result.CardCount);
        Assert.Contains(result.Lines, l => l.CardName == "Counterspell" && l.RejoinedStack && l.LotId == stack && l.Issue == "None");
        Assert.Contains(result.Lines, l => l.CardName == "Swords to Plowshares" && l.Issue == "NoOrigin" && l.DestinationId == _boxId);
        Assert.Contains(result.Lines, l => l.CardName == "Sol Ring" && l.Page == 2 && l.Slot == 5);
    }

    [Fact]
    public void Deconstruct_ByDefault_SendsUnknownOriginsToBulk()
    {
        var unknown = Lot("Swords to Plowshares", _deckId);

        Deconstruct();

        Assert.Equal(_bulkId, Get(unknown)!.LocationId);
    }

    [Fact]
    public void Deconstruct_WhenThePocketHoldsAnotherCard_SendsTheCopyToBulk_AndNamesTheOccupant()
    {
        var pocket = Lot("Sol Ring", _binderId, page: 1, slot: 1);
        _cards.MoveCardsToContainer([pocket], _deckId);
        var occupant = Lot("Mana Crypt", _binderId, page: 1, slot: 1);

        var line = Assert.Single(Deconstruct().Lines);

        Assert.Equal(_bulkId, Get(pocket)!.LocationId);
        Assert.Null(Get(pocket)!.Page);
        Assert.Equal((_binderId, 1), (Get(occupant)!.LocationId!.Value, Get(occupant)!.Slot!.Value));
        Assert.Equal(("SlotTaken", "Mana Crypt", "Trade Binder", 1), (line.Issue, line.SlotTakenBy, line.OriginName, line.OriginSlot));
    }

    [Fact]
    public void Deconstruct_SendsCopiesFromReadOnlyDeletedOrOtherGameLocationsToBulk()
    {
        int shopId;
        using (var ctx = new OmniCardDbContext(_opts))
        {
            var shop = new Site { Name = "Shop" };
            ctx.Sites.Add(shop);
            ctx.SaveChanges();
            shopId = shop.Id;
        }
        var shopBinder = _containers.Create("Shop Binder", ContainerType.Binder, siteId: shopId).Id;
        var gone = _containers.Create("Old Box", ContainerType.Box).Id;
        var readOnly = Lot("Sol Ring", shopBinder);
        var deleted = Lot("Counterspell", gone);
        var otherGame = Lot("Brainstorm", _boxId);
        _cards.MoveCardsToContainer([readOnly, deleted, otherGame], _deckId);
        _containers.Delete(gone, moveCardsToBulk: true);
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.StorageContainers.Find(_boxId)!.Game = CardGame.Pokemon;
            ctx.SaveChanges();
        }
        var access = SiteAccess.FromGrants(new Dictionary<int, SiteAccessLevel> { [shopId] = SiteAccessLevel.Read });

        var lines = Deconstruct(access: access).Lines;

        Assert.All(new[] { readOnly, deleted, otherGame }, id => Assert.Equal(_bulkId, Get(id)!.LocationId));
        Assert.Equal("NoAccess", lines.Single(l => l.CardName == "Sol Ring").Issue);
        Assert.Equal("LocationGone", lines.Single(l => l.CardName == "Counterspell").Issue);
        Assert.Equal("WrongGame", lines.Single(l => l.CardName == "Brainstorm").Issue);
    }

    [Fact]
    public void Deconstruct_SavesAReport_AndAListToRebuildFrom()
    {
        var lot = Lot("Sol Ring", _binderId, page: 1, slot: 1);
        _cards.MoveCardsToContainer([lot], _deckId);

        var result = Deconstruct(listName: "Commander (deconstructed)");

        var summary = Assert.Single(_returns.Reports(_deckId));
        Assert.Equal((result.ReportId, true, 1, "tester"), (summary.Id, summary.WholeDeck, summary.CardCount, summary.CreatedBy));
        var (report, lines) = _returns.Report(_deckId, result.ReportId)!.Value;
        Assert.Equal("Trade Binder", Assert.Single(lines).DestinationName);
        Assert.Null(_returns.Report(_otherDeckId, result.ReportId));

        using var ctx = new OmniCardDbContext(_opts);
        var list = ctx.CardLists.Single(l => l.Id == result.ListId);
        Assert.Equal("Commander (deconstructed)", list.Name);
        var item = ctx.CardListItems.Single(i => i.CardListId == list.Id);
        Assert.Equal((lot, 1), (item.SourceLotId!.Value, item.Quantity));

        var pdf = Path.Combine(Path.GetTempPath(), $"put-back-{Guid.NewGuid():N}.pdf");
        try
        {
            new DeckReturnPdfExporter().Export(report, lines, pdf);
            Assert.True(new FileInfo(pdf).Length > 0);
        }
        finally
        {
            File.Delete(pdf);
        }
    }

    [Fact]
    public void Preview_ChangesNothing()
    {
        var lot = Lot("Sol Ring", _binderId, page: 1, slot: 1);
        _cards.MoveCardsToContainer([lot], _deckId);

        var preview = _returns.Preview(_deckId, new DeckReturnRequest(), SiteAccess.Unrestricted);

        Assert.True(preview.WholeDeck);
        Assert.Equal(("Bulk", 1), (preview.FallbackLocationName, preview.CardCount));
        Assert.Equal(_binderId, Assert.Single(preview.Lines).DestinationId);
        Assert.Equal(_deckId, Get(lot)!.LocationId);
        Assert.Empty(_returns.Reports(_deckId));
    }

    [Fact]
    public void ReturningSomeCopies_SplitsTheStack_AndDropsTheDeckRoleTags()
    {
        var stack = Lot("Lightning Bolt", _boxId, quantity: 3);
        _cards.MoveCardsToContainer([stack], _deckId);
        _tags.AddTagToLots([stack], DeckBoxSyncService.SideboardTag);
        _tags.AddTagToLots([stack], "favorite");

        var result = _returns.Apply(_deckId, new DeckReturnRequest { Lots = [new DeckSyncPickDto(stack, 1)] },
            SiteAccess.Unrestricted, null);

        Assert.False(result.Lines.Single().RejoinedStack);
        Assert.Equal(2, Get(stack)!.Quantity);
        Assert.Equal(_deckId, Get(stack)!.LocationId);
        var returned = Assert.Single(LotsIn(_boxId));
        Assert.Equal(1, returned.Quantity);
        Assert.Null(returned.OriginLocationId);

        _returns.Apply(_deckId, new DeckReturnRequest { Lots = [new DeckSyncPickDto(stack, 2)] }, SiteAccess.Unrestricted, null);
        Assert.Equal(_boxId, Get(stack)!.LocationId);
        Assert.Equal(["favorite"], _tags.GetTagsByLots([stack])[stack]);
    }

    [Fact]
    public void ReturningWholeLotsById_LeavesTheRestOfTheDeck()
    {
        var stack = Lot("Lightning Bolt", _boxId, quantity: 3);
        var other = Lot("Sol Ring", _binderId);
        _cards.MoveCardsToContainer([stack, other], _deckId);

        var result = _returns.Apply(_deckId, new DeckReturnRequest { LotIds = [stack] }, SiteAccess.Unrestricted, null);

        Assert.Equal(3, result.CardCount);
        Assert.Equal(_boxId, Get(stack)!.LocationId);
        Assert.Equal(_deckId, Get(other)!.LocationId);
        Assert.False(Assert.Single(_returns.Reports(_deckId)).WholeDeck);
    }

    [Fact]
    public void Apply_RefusesADeckBoxAsTheFallback_AndANonDeckBox()
    {
        Lot("Sol Ring", _deckId);

        Assert.Throws<DeckReturnService.DeckReturnException>(() => Deconstruct(fallback: _otherDeckId));
        Assert.Throws<DeckReturnService.DeckReturnException>(() =>
            _returns.Apply(_binderId, new DeckReturnRequest(), SiteAccess.Unrestricted, null));
    }

    [Fact]
    public void Apply_OnAnEmptyDeck_Throws()
    {
        Assert.Throws<DeckReturnService.DeckReturnException>(() => Deconstruct());
    }

    [Fact]
    public void Report_RoundTripsTheIssueAsAName()
    {
        Lot("Sol Ring", _deckId);
        var result = Deconstruct();

        using var ctx = new OmniCardDbContext(_opts);
        var json = ctx.DeckReturnReports.Single(r => r.Id == result.ReportId).LinesJson;
        Assert.Equal(DeckReturnIssue.NoOrigin, JsonSerializer.Deserialize<List<DeckReturnLine>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))![0].Issue);
    }

    // --- deck sync "Return" ---

    [Fact]
    public async Task SyncReturnAction_SendsCutCopiesBackWhereTheyCameFrom()
    {
        _sync.SetLink(_deckId, "https://moxfield.com/decks/abc");
        var lot = Lot("Sol Ring", _binderId, page: 4, slot: 2);
        _cards.MoveCardsToContainer([lot], _deckId);
        _decklists.FetchResult = ("Test Deck", [new DecklistEntry(1, "Island", null, null)]);

        var cut = Assert.Single((await _sync.PreviewAsync(_deckId, SiteAccess.Unrestricted)).Cuts);
        Assert.Equal("Trade Binder", cut.OriginName);

        var result = _sync.Apply(_deckId, new DeckSyncApplyRequest
        {
            Cuts = [new DeckSyncCutDecisionDto(lot, 1, "Return", null)],
        }, SiteAccess.Unrestricted);

        Assert.Equal(1, result.Removed);
        Assert.NotNull(result.ReturnReportId);
        Assert.Equal((_binderId, 4, 2), (Get(lot)!.LocationId!.Value, Get(lot)!.Page!.Value, Get(lot)!.Slot!.Value));
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
        public void SetPendingDataDirectory(string path) { }
        public bool IsMigrationPending => false;
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
