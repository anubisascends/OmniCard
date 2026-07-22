using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class RiftboundDownloadTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<RiftboundDbContext> _factory;
    private readonly string _dataDir;

    public RiftboundDownloadTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<RiftboundDbContext>().UseSqlite(_connection).Options;
        _factory = new TestFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete();

        _dataDir = Path.Combine(Path.GetTempPath(), "rift-dl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private const string SetListJson = """
    {"items":[{"id":"s1","name":"Origins","set_id":"OGN","card_count":2,
      "tcgplayer_id":"24344","cardmarket_id":"6286","published_on":"2025-10-31T00:00:00"}],
      "total":1,"page":1,"size":100,"pages":1}
    """;

    // OGN has 2 cards spread over 2 pages (size=1) to exercise paging.
    private static string CardsPage(int page) => page switch
    {
        1 => """
        {"items":[{"id":"c1","name":"Cull the Weak","riftbound_id":"ogn-209-298","tcgplayer_id":"653002",
          "collector_number":209,"attributes":{"energy":2,"might":null,"power":1},
          "classification":{"type":"Spell","supertype":null,"rarity":"Common","domain":["Order"]},
          "text":{"rich":null,"plain":"kill","flavour":null},"set":{"set_id":"OGN","label":"Origins"},
          "media":{"image_url":"https://cdn/c1.png","artist":"A","accessibility_text":null},
          "tags":[],"orientation":"portrait",
          "metadata":{"clean_name":"Cull the Weak","updated_on":null,"alternate_art":false,
            "overnumbered":false,"signature":false},"new":false}],
          "total":2,"page":1,"size":1,"pages":2}
        """,
        _ => """
        {"items":[{"id":"c2","name":"Vex","riftbound_id":"ogn-310*-298","tcgplayer_id":null,
          "collector_number":310,"attributes":{"energy":4,"might":null,"power":4},
          "classification":{"type":"Legend","supertype":null,"rarity":"Epic","domain":["Body","Order"]},
          "text":{"rich":null,"plain":null,"flavour":null},"set":{"set_id":"OGN","label":"Origins"},
          "media":{"image_url":"https://cdn/c2.png","artist":"B","accessibility_text":null},
          "tags":[],"orientation":"landscape",
          "metadata":{"clean_name":"Vex","updated_on":null,"alternate_art":true,
            "overnumbered":true,"signature":false},"new":false}],
          "total":2,"page":2,"size":1,"pages":2}
        """
    };

    private RiftboundService CreateService()
    {
        var handler = new RoutingHandler(uri =>
        {
            if (uri.Contains("/sets")) return SetListJson;
            if (uri.Contains("/cards"))
            {
                var page = uri.Contains("page=2") ? 2 : 1;
                return CardsPage(page);
            }
            return null;
        });
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        return new RiftboundService(
            new FakeHttpClientFactory(handler),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<RiftboundService>.Instance);
    }

    [Fact]
    public async Task DownloadBulkData_PagesThroughAllCards_AndMaps()
    {
        var svc = CreateService();
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        var rows = ctx.Cards.OrderBy(c => c.CollectorNumber).ToList();
        Assert.Equal(2, rows.Count);

        var cull = rows.Single(c => c.Id == "c1");
        Assert.Equal(209, cull.CollectorNumber);
        Assert.Equal("Cull the Weak", cull.Name);
        Assert.Equal("OGN", cull.SetId);
        Assert.Equal("Origins", cull.SetName);
        Assert.Equal("Spell", cull.CardType);
        Assert.Equal("Order", cull.Domain);
        Assert.Equal("portrait", cull.Orientation);
        Assert.Equal("https://cdn/c1.png", cull.CardImageUri);
        Assert.False(cull.AlternateArt);

        var vex = rows.Single(c => c.Id == "c2");
        Assert.Equal("Body/Order", vex.Domain);
        Assert.Equal("landscape", vex.Orientation);
        Assert.True(vex.AlternateArt);
        Assert.True(vex.Overnumbered);

        Assert.Equal(RiftboundDbContext.RiftboundSchemaVersion, ctx.GetSchemaVersion());
    }

    [Fact]
    public async Task UpdatePrices_IsNoOp()
    {
        var svc = CreateService();
        await svc.DownloadBulkDataAsync();
        // Should not throw and should not alter rows.
        await svc.UpdatePricesAsync();
        using var ctx = _factory.CreateDbContext();
        Assert.Equal(2, ctx.Cards.Count());
    }

    private class RoutingHandler(Func<string, string?> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = route(request.RequestUri!.ToString());
            var resp = body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            return Task.FromResult(resp);
        }
    }

    private class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private class TestFactory(DbContextOptions<RiftboundDbContext> options) : IDbContextFactory<RiftboundDbContext>
    {
        public RiftboundDbContext CreateDbContext() => new(options);
    }
}
