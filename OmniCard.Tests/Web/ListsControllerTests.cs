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
using OmniCard.Web.Api.Controllers;

namespace OmniCard.Tests.Web;

/// <summary>Covers the SPA's saved-card-list endpoints: list CRUD, item read/qty/remove, and the
/// commit-to-location write (which lands lots via WebBinderCardService and consumes the list).</summary>
public class ListsControllerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly ListsController _controller;
    private readonly StorageContainerService _containers;
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
        var binderCards = new WebBinderCardService(factory, new StubDataPath());
        var imageCache = new CardImageCacheService(new StubDataPath(), new StubHttpClientFactory(), NullLogger<CardImageCacheService>.Instance);
        _controller = new ListsController(listService, _decklists, binderCards, cardService, imageCache);
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

    [Fact]
    public void Commit_WritesLots_And_DeletesList()
    {
        var loc = _containers.Create("Box", ContainerType.Box).Id;
        var list = Value(_controller.Create(new CreateListRequest { Name = "ToBuy", Game = "Mtg" }));
        SeedItem(list.Id, "Bolt", qty: 2);
        SeedItem(list.Id, "Island", qty: 1, foil: true);

        var result = Value(_controller.Commit(list.Id, new CommitListRequest { ContainerId = loc, Condition = "LP" }));
        Assert.Equal(2, result.Imported);
        Assert.True(result.ListDeleted);

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal(3, ctx.Lots.Where(l => l.LocationId == loc).Sum(l => l.Quantity)); // 2 + 1
        Assert.False(ctx.CardLists.Any(l => l.Id == list.Id)); // list consumed
    }

    [Fact]
    public void Commit_NoLocation_Returns400()
    {
        var list = Value(_controller.Create(new CreateListRequest { Name = "L", Game = "Mtg" }));
        SeedItem(list.Id, "Bolt");
        Assert.IsType<BadRequestObjectResult>(
            _controller.Commit(list.Id, new CommitListRequest { ContainerId = 0 }).Result);
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
        public DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries, CardGame game) => throw new NotImplementedException();
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
