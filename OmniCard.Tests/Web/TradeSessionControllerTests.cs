using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web;
using OmniCard.Web.Api;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>Tests the SPA trade builder API (port of the retired Razor TradeSession page): building a
/// draft in the shared trades folder and applying it to the collection on finalize.</summary>
public class TradeSessionControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OmniCardDbContext> _factory;
    private readonly WebDataPathService _paths;
    private readonly ICardService _noGames = new WebCardService([]);
    private readonly ITradeImportService _import;

    public TradeSessionControllerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_connection).Options;
        _factory = new TestDbContextFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();

        _paths = new WebDataPathService(Path.Combine(Path.GetTempPath(), "omnicard-tradectl-" + Guid.NewGuid()));
        _import = new TradeImportService(_factory, _paths, NullLogger<TradeImportService>.Instance);
    }

    public void Dispose()
    {
        _connection.Dispose();
        var root = Path.GetDirectoryName(_paths.TradesDirectory);
        if (root is not null && Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private TradeSessionController NewController(out DefaultHttpContext http)
    {
        http = new DefaultHttpContext();
        return new TradeSessionController(_factory, _paths, _noGames, _import)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static void SetActiveCookie(DefaultHttpContext http, Guid id) =>
        http.Request.Headers["Cookie"] = $"{TradeSessionCookie.CookieName}={id}";

    private Guid OnlyDraftId() =>
        Guid.Parse(Path.GetFileName(Directory.GetDirectories(_paths.TradesDirectory).Single()));

    private int SeedLot(string name = "Lightning Bolt")
    {
        using var ctx = _factory.CreateDbContext();
        var p = new Product
        {
            Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = "bolt",
            Name = name, SetName = "Alpha", SetCode = "LEA", CollectorNumber = "161", Rarity = "common",
        };
        ctx.Products.Add(p);
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = p.Id };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    [Fact]
    public void Start_CreatesEmptyDraft()
    {
        var controller = NewController(out _);

        var result = controller.Start();

        Assert.IsType<OkObjectResult>(result);
        var folder = Directory.GetDirectories(_paths.TradesDirectory).Single();
        Assert.True(File.Exists(Path.Combine(folder, "trade.json")));
    }

    [Fact]
    public void AddOwned_AppendsItemToDraft()
    {
        var lotId = SeedLot();
        var controller = NewController(out var http);

        controller.AddOwned(new TradeSessionController.AddOwnedRequest(lotId));

        var id = OnlyDraftId();
        var jsonPath = Path.Combine(_paths.TradesDirectory, id.ToString(), "trade.json");
        var record = System.Text.Json.JsonSerializer.Deserialize<TradeSessionRecord>(File.ReadAllText(jsonPath))!;
        var item = Assert.Single(record.OutgoingItems);
        Assert.Equal(lotId, item.LotId);
        Assert.Equal("Lightning Bolt", item.CardName);
    }

    [Fact]
    public async Task Finalize_AppliesTrade_MarksLotTradedAndRecordsHistory()
    {
        var lotId = SeedLot();
        var controller = NewController(out var http);

        // Auto-create a draft + add the card, then make that draft the active one for finalize.
        controller.AddOwned(new TradeSessionController.AddOwnedRequest(lotId));
        SetActiveCookie(http, OnlyDraftId());

        var result = await controller.Finalize(note: "for a dual land", receivedValue: 50m, receivedPhoto: null);

        Assert.IsType<OkObjectResult>(result);
        using var ctx = _factory.CreateDbContext();
        var lot = ctx.Lots.Single(l => l.Id == lotId);
        Assert.True(lot.IsTraded);
        var session = Assert.Single(ctx.TradeSessions);
        Assert.Equal(50m, session.ReceivedValue);
        var trade = Assert.Single(ctx.Trades);
        Assert.Equal(lotId, trade.OriginalLotId);
        Assert.Equal("Lightning Bolt", trade.CardName);
    }

    [Fact]
    public async Task Finalize_EmptyTrade_ReturnsBadRequest_AndDoesNotApply()
    {
        var controller = NewController(out var http);
        controller.Start();
        SetActiveCookie(http, OnlyDraftId());

        var result = await controller.Finalize(note: "nothing", receivedValue: null, receivedPhoto: null);

        Assert.IsType<BadRequestObjectResult>(result);
        using var ctx = _factory.CreateDbContext();
        Assert.Empty(ctx.TradeSessions);
    }

    [Fact]
    public void Cancel_DeletesDraftFolder()
    {
        var controller = NewController(out var http);
        controller.Start();
        var id = OnlyDraftId();
        SetActiveCookie(http, id);

        controller.Cancel();

        Assert.False(Directory.Exists(Path.Combine(_paths.TradesDirectory, id.ToString())));
    }

    private sealed class TestDbContextFactory(DbContextOptions<OmniCardDbContext> options)
        : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
