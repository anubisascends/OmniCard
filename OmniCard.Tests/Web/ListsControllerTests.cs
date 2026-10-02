using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Web.Services;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Settings;
using OmniCard.Shared.Storage;
using OmniCard.Collection.Inventory;
using OmniCard.Collection.Lists;
using OmniCard.Audit.Exporters;
using OmniCard.Shared.Collection;
using OmniCard.Web.Api.Controllers;

namespace OmniCard.Tests.Web;

/// <summary>Covers the SPA's saved-card-list endpoints: list CRUD, item read/qty/remove, owned counts,
/// the pick/buy/whole-list prints, and fulfillment (move owned copies + create missing ones via
/// WebBinderCardService, consuming the list).</summary>
public class ListsControllerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly ListsController _controller;
    private readonly StorageContainerService _containers;
    private readonly WebBinderCardService _binderCards;
    private readonly StubDecklists _decklists = new();

    public ListsControllerTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();

        var factory = new MockFactory(_opts);
        var cardService = new WebCardService([]);
        var listService = new ListService(factory, cardService);
        _binderCards = new WebBinderCardService(factory, new StubDataPath());
        var imageCache = new CardImageCacheService(new StubDataPath(), new StubHttpClientFactory(), NullLogger<CardImageCacheService>.Instance);
        _controller = new ListsController(listService, _decklists, _binderCards, cardService, imageCache,
            new ListFulfillmentPlanner(factory), new DecklistPrintExporter());
        _containers = new StorageContainerService(factory);
    }

    public void Dispose() => _conn.Dispose();

    private static T Value<T>(ActionResult<T> r) => r.Result is ObjectResult o ? (T)o.Value! : r.Value!;

    private int SeedItem(int listId, string name, int qty = 1, bool foil = false)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var item = new CardListItem
        {
            CardListId = listId,
            GameCardId = name.ToLowerInvariant(),
            CardName = name,
            SetCode = "SET",
            CollectorNumber = "1",
            IsFoil = foil,
            Quantity = qty,
            Source = ListItemSource.Manual,
        };
        ctx.CardListItems.Add(item);
        ctx.SaveChanges();
        return item.Id;
    }

    [Fact]
    public void Create_List_ShowsUp_And_RenameWorks()
    {
        var created = Value(_controller.Create(new CreateListRequest { Name = "Wants", Game = "Mtg" }));
        Assert.True(created.Id > 0);
        Assert.Equal("Wants", created.Name);

        Assert.IsType<NoContentResult>(_controller.Rename(created.Id, new RenameRequest { Name = "Trade Binder" }));

        var listed = Value(_controller.Get("Mtg"));
        Assert.Single(listed);
        Assert.Equal("Trade Binder", listed[0].Name);
    }

    [Fact]
    public void Items_Read_SetQuantity_Remove()
    {
        var list = Value(_controller.Create(new CreateListRequest { Name = "L", Game = "Mtg" }));
        var itemId = SeedItem(list.Id, "Bolt", qty: 1);

        var items = Value(_controller.Items(list.Id));
        Assert.Single(items);

        Assert.IsType<NoContentResult>(_controller.SetQuantity(itemId, new SetQuantityRequest { Quantity = 4 }));
        Assert.Equal(4, Value(_controller.Items(list.Id))[0].Quantity);

        Assert.IsType<NoContentResult>(_controller.RemoveItem(itemId));
        Assert.Empty(Value(_controller.Items(list.Id)));
    }

    /// <summary>Seeds one owned lot (a stack of <paramref name="qty"/>) whose printing matches <see cref="SeedItem"/>'s.</summary>
    private void SeedOwned(string name, int qty, int containerId, bool foil = false) =>
        _binderCards.ImportCollectionCards(
        [
            new CollectionCard
            {
                Game = CardGame.Mtg, GameCardId = name.ToLowerInvariant(), Name = name, SetCode = "SET", Number = "1",
                IsFoil = foil, Quantity = qty, Condition = "NM", ContainerId = containerId, DateAdded = DateTime.UtcNow,
            },
        ], skipDuplicates: false);

    private int CopiesIn(int containerId, string gameCardId)
    {
        using var ctx = new OmniCardDbContext(_opts);
        return ctx.Lots.Where(l => l.LocationId == containerId && l.Product.GameCardId == gameCardId).Sum(l => (int?)l.Quantity) ?? 0;
    }

    [Fact]
    public void Items_OwnedQuantity_MatchesExactPrintingAndFinish()
    {
        var src = _containers.Create("Src", ContainerType.Box).Id;
        SeedOwned("Bolt", 1, src);
        var list = Value(_controller.Create(new CreateListRequest { Name = "L", Game = "Mtg" }));
        SeedItem(list.Id, "Bolt", qty: 2);
        SeedItem(list.Id, "Bolt", qty: 1, foil: true); // the owned copy is non-foil

        var items = Value(_controller.Items(list.Id));
        var plain = items.Single(i => !i.IsFoil);
        var foil = items.Single(i => i.IsFoil);
        Assert.Equal(1, plain.OwnedQuantity);
        Assert.True(plain.InCollection);
        Assert.Equal(0, foil.OwnedQuantity);
        Assert.False(foil.InCollection);
    }

    [Fact]
    public void Fulfill_MoveAndAdd_InOneClick_SplitsStack_CreatesMissing_DeletesList()
    {
        var src = _containers.Create("Src", ContainerType.Box).Id;
        var deck = _containers.Create("Deck", ContainerType.Box).Id;
        var buys = _containers.Create("New", ContainerType.Box).Id;
        SeedOwned("Bolt", 3, src);
        var list = Value(_controller.Create(new CreateListRequest { Name = "L", Game = "Mtg" }));
        SeedItem(list.Id, "Bolt", qty: 2);
        SeedItem(list.Id, "Island", qty: 1);

        var result = Value(_controller.Fulfill(list.Id,
            new FulfillListRequest { MoveToContainerId = deck, AddToContainerId = buys, Condition = "LP" }));

        Assert.Equal(2, result.Moved);
        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Remaining);
        Assert.True(result.ListDeleted);
        Assert.Equal(2, CopiesIn(deck, "bolt"));
        Assert.Equal(1, CopiesIn(src, "bolt")); // remainder of the stack stays put
        Assert.Equal(1, CopiesIn(buys, "island"));
        Assert.Equal(0, CopiesIn(buys, "bolt")); // owned copies are moved, never duplicated
        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal("LP", ctx.Lots.Single(l => l.Product.GameCardId == "island").Condition);
        Assert.False(ctx.CardLists.Any(l => l.Id == list.Id));
    }

    [Fact]
    public void Fulfill_MoveOnly_LeavesShortfallAwaitingPurchase_ThenAddFinishesIt()
    {
        var src = _containers.Create("Src", ContainerType.Box).Id;
        var deck = _containers.Create("Deck", ContainerType.Box).Id;
        SeedOwned("Bolt", 1, src);
        var list = Value(_controller.Create(new CreateListRequest { Name = "L", Game = "Mtg" }));
        SeedItem(list.Id, "Bolt", qty: 3);

        var moved = Value(_controller.Fulfill(list.Id, new FulfillListRequest { MoveToContainerId = deck }));
        Assert.Equal(1, moved.Moved);
        Assert.Equal(2, moved.Remaining);
        Assert.False(moved.ListDeleted);

        // The moved copy no longer counts toward what's left: the remainder is all to buy.
        var left = Assert.Single(Value(_controller.Items(list.Id)));
        Assert.Equal(2, left.Quantity);
        Assert.Equal(0, left.OwnedQuantity);
        Assert.True(left.AwaitingPurchase);

        var added = Value(_controller.Fulfill(list.Id, new FulfillListRequest { AddToContainerId = deck }));
        Assert.Equal(2, added.Added);
        Assert.True(added.ListDeleted);
        Assert.Equal(3, CopiesIn(deck, "bolt"));
    }

    [Fact]
    public void Fulfill_AddOnly_KeepsOwnedItemsOnTheList()
    {
        var src = _containers.Create("Src", ContainerType.Box).Id;
        var buys = _containers.Create("New", ContainerType.Box).Id;
        SeedOwned("Bolt", 1, src);
        var list = Value(_controller.Create(new CreateListRequest { Name = "L", Game = "Mtg" }));
        SeedItem(list.Id, "Bolt", qty: 1);
        SeedItem(list.Id, "Island", qty: 2);

        var result = Value(_controller.Fulfill(list.Id, new FulfillListRequest { AddToContainerId = buys }));

        Assert.Equal(0, result.Moved);
        Assert.Equal(2, result.Added);
        Assert.False(result.ListDeleted);
        var left = Assert.Single(Value(_controller.Items(list.Id)));
        Assert.Equal("Bolt", left.CardName);
        Assert.Equal(1, CopiesIn(src, "bolt")); // owned copy untouched
    }

    [Fact]
    public void Fulfill_NoLocation_Returns400()
    {
        var list = Value(_controller.Create(new CreateListRequest { Name = "L", Game = "Mtg" }));
        SeedItem(list.Id, "Bolt");
        Assert.IsType<BadRequestObjectResult>(_controller.Fulfill(list.Id, new FulfillListRequest()).Result);
    }

    [Fact]
    public void PrintEndpoints_ReturnPdfs()
    {
        var src = _containers.Create("Src", ContainerType.Box).Id;
        SeedOwned("Bolt", 1, src);
        var list = Value(_controller.Create(new CreateListRequest { Name = "Wants / Test", Game = "Mtg" }));
        SeedItem(list.Id, "Bolt", qty: 2);

        foreach (var result in new[] { _controller.PrintPdf(list.Id), _controller.PickListPdf(list.Id), _controller.BuyListPdf(list.Id) })
        {
            var file = Assert.IsType<FileContentResult>(result);
            Assert.Equal("application/pdf", file.ContentType);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(file.FileContents, 0, 4));
        }
        Assert.IsType<NotFoundResult>(_controller.PickListPdf(9999));
    }

    [Fact]
    public void Create_BlankName_Returns400()
    {
        Assert.IsType<BadRequestObjectResult>(
            _controller.Create(new CreateListRequest { Name = " ", Game = "Mtg" }).Result);
    }

    [Fact]
    public async Task ImportUrl_BlankUrl_Returns400()
    {
        var result = await _controller.ImportUrl(new ImportListUrlRequest { Url = " ", Game = "Mtg" });
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ImportUrl_UnfetchableUrl_Returns400()
    {
        _decklists.FetchResult = null; // simulates a URL that couldn't be fetched/parsed
        var result = await _controller.ImportUrl(
            new ImportListUrlRequest { Url = "https://moxfield.com/decks/nope", Game = "Mtg" });
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    /// <summary>Stub decklist service — the URL-fetch result is set per test; parsing/check members are unused here.</summary>
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

    // Image caching only touches the filesystem for the read paths the controller uses (DisplayUrl/
    // PreferCached), so this never actually gets called — it just satisfies the constructor.
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
