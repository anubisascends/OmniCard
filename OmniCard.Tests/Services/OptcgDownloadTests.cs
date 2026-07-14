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

public class OptcgDownloadTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;
    private readonly string _dataDir;

    public OptcgDownloadTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OptcgDbContext>().UseSqlite(_connection).Options;
        _factory = new TestOptcgDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete(); // already migrated so the ctor does not wipe

        _dataDir = Path.Combine(Path.GetTempPath(), "optcg-dl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private const string SetListJson = """{"data":[{"code":"OP01","name":"Romance Dawn","released_at":null,"card_count":1}]}""";

    private const string SetDetailJson = """
    {"data":{"code":"OP01","name":"Romance Dawn","card_count":1,"products":[],"cards":[
      {"card_number":"OP01-001","name":"Zoro","language":"en","set":"OP01","set_name":"Romance Dawn",
       "released_at":null,"released":true,"card_type":"Leader","rarity":"L","color":["Red","Green"],
       "cost":null,"power":5000,"counter":null,"life":5,"attribute":["Slash"],"types":["Straw Hat Crew"],
       "effect":"Text.","trigger":null,"block":null,"variants":[
         {"index":0,"name":null,"label":"Standard","artist":null,"crop_focus":{"x":null,"y":null},
          "product":{"id":null,"slug":null,"name":null,"set_code":null,"released_at":null},
          "images":{"stock":{"full":"https://cdn/stock0.png","thumb":null},"scan":{"display":null,"full":null,"thumb":null}},
          "errata":[],"market":{"tcgplayer_url":null,"market_price":"6.00","low_price":"1.46","mid_price":"6.80","high_price":"34.99"}},
         {"index":1,"name":null,"label":"Alt","artist":"Artist X","crop_focus":{"x":null,"y":null},
          "product":{"id":null,"slug":null,"name":null,"set_code":null,"released_at":null},
          "images":{"stock":{"full":"https://cdn/stock1.png","thumb":null},"scan":{"display":"https://cdn/scan1.png","full":null,"thumb":null}},
          "errata":[],"market":{"tcgplayer_url":null,"market_price":"40.00","low_price":"25.00","mid_price":"41.00","high_price":"99.00"}}
       ]}
    ]}}
    """;

    private OptcgService CreateService()
    {
        var handler = new RoutingHandler(uri =>
        {
            if (uri.EndsWith("/v1/sets")) return SetListJson;
            if (uri.EndsWith("/v1/sets/OP01")) return SetDetailJson;
            return null;
        });
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        return new OptcgService(
            new FakeHttpClientFactory(handler),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<OptcgService>.Instance);
    }

    [Fact]
    public async Task DownloadBulkData_FlattensVariants_WithUidScheme()
    {
        var svc = CreateService();
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        var rows = ctx.Cards.OrderBy(c => c.CardSetId).ToList();
        Assert.Equal(2, rows.Count);

        var baseRow = rows.Single(c => c.CardSetId == "OP01-001");
        Assert.Equal("OP01-001", baseRow.CardNumber);
        Assert.Equal(0, baseRow.VariantIndex);
        Assert.Equal("Zoro", baseRow.CardName);
        Assert.Equal("OP01", baseRow.SetId);
        Assert.Equal("Red/Green", baseRow.CardColor);
        Assert.Equal("Straw Hat Crew", baseRow.SubTypes);
        Assert.Equal("Slash", baseRow.Attribute);
        Assert.Equal("5000", baseRow.CardPower);
        Assert.Equal("5", baseRow.Life);
        Assert.Null(baseRow.CardCost);
        Assert.Equal("Text.", baseRow.CardText);
        Assert.Equal("https://cdn/stock0.png", baseRow.CardImageUri); // scan null -> stock.full
        Assert.Equal(6.00m, baseRow.MarketPrice);
        Assert.Equal(1.46m, baseRow.InventoryPrice);

        var altRow = rows.Single(c => c.CardSetId == "OP01-001_p1");
        Assert.Equal("OP01-001", altRow.CardNumber);
        Assert.Equal(1, altRow.VariantIndex);
        Assert.Equal("Artist X", altRow.Artist);
        Assert.Equal("https://cdn/scan1.png", altRow.CardImageUri); // scan.display preferred
        Assert.Equal(40.00m, altRow.MarketPrice);

        // Version marker flips to migrated after a successful download.
        Assert.Equal(OptcgDbContext.PoneglyphSchemaVersion, ctx.GetSchemaVersion());
    }

    private class RoutingHandler(Func<string, string?> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = route(request.RequestUri!.ToString());
            var resp = body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            return Task.FromResult(resp);
        }
    }

    private class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private class TestOptcgDbFactory(DbContextOptions<OptcgDbContext> options) : IDbContextFactory<OptcgDbContext>
    {
        public OptcgDbContext CreateDbContext() => new(options);
    }
}
