using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Models;
using OmniCard.CardMatching;

namespace OmniCard.Tests.Services;

public class PriceResolutionTests : IDisposable
{
    private readonly SqliteConnection _scryfallConnection;
    private readonly DbContextOptions<ScryfallDbContext> _scryfallOptions;
    private readonly SqliteConnection _optcgConnection;
    private readonly DbContextOptions<OptcgDbContext> _optcgOptions;

    public PriceResolutionTests()
    {
        _scryfallConnection = new SqliteConnection("Data Source=:memory:");
        _scryfallConnection.Open();
        _scryfallOptions = new DbContextOptionsBuilder<ScryfallDbContext>()
            .UseSqlite(_scryfallConnection)
            .Options;
        using var scryfallCtx = new ScryfallDbContext(_scryfallOptions);
        scryfallCtx.Database.EnsureCreated();

        _optcgConnection = new SqliteConnection("Data Source=:memory:");
        _optcgConnection.Open();
        _optcgOptions = new DbContextOptionsBuilder<OptcgDbContext>()
            .UseSqlite(_optcgConnection)
            .Options;
        using var optcgCtx = new OptcgDbContext(_optcgOptions);
        optcgCtx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _scryfallConnection.Dispose();
        _optcgConnection.Dispose();
    }

    [Fact]
    public void ScryfallService_GetCurrentPrice_ReturnsUsd()
    {
        var cardId = Guid.NewGuid();
        using (var ctx = new ScryfallDbContext(_scryfallOptions))
        {
            ctx.Cards.Add(new Card
            {
                Id = cardId,
                Name = "Test Card",
                Prices = new Prices { Usd = "1.50", UsdFoil = "3.00" },
                // required fields
                SetCode = "tst", SetName = "Test", CollectorNumber = "1",
                Rarity = "rare", SetId = Guid.NewGuid(), SetType = "expansion",
                SetUri = "", SetSearchUri = "", ScryfallSetUri = "",
                RulingsUri = "", PrintsSearchUri = "", TypeLine = "Creature",
                BorderColor = "black", Frame = "2015",
            });
            ctx.SaveChanges();
        }

        var dbFactory = new MockScryfallDbContextFactory(_scryfallOptions);
        var service = new ScryfallService(
            new MockHttpClientFactory(new MockNoOpHandler()),
            dbFactory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            new SetSymbolCache(new MockHttpClientFactory(new MockNoOpHandler()), new DataPathService(Path.GetTempPath()), NullLogger<SetSymbolCache>.Instance),
            Options.Create(new ScryfallSettings()),
            NullLogger<ScryfallService>.Instance,
            new DataPathService(Path.GetTempPath()));

        Assert.Equal(1.50m, service.GetCurrentPrice(cardId.ToString(), isFoil: false));
        Assert.Equal(3.00m, service.GetCurrentPrice(cardId.ToString(), isFoil: true));
    }

    [Fact]
    public void OptcgService_GetCurrentPrice_ReturnsMarketPrice()
    {
        using (var ctx = new OptcgDbContext(_optcgOptions))
        {
            ctx.Cards.Add(new OptcgCard
            {
                CardSetId = "OP01-001",
                CardName = "Roronoa Zoro",
                SetId = "OP01", SetName = "Romance Dawn",
                Rarity = "SR", CardColor = "Green", CardType = "Character",
                MarketPrice = 5.99m,
            });
            ctx.SaveChanges();
        }

        var dbFactory = new MockOptcgDbContextFactory(_optcgOptions);
        var dataPath = new Moq.Mock<OmniCard.Interfaces.IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(Path.GetTempPath());
        var service = new OptcgService(
            new MockHttpClientFactory(new MockNoOpHandler()),
            dbFactory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<OptcgService>.Instance);

        Assert.Equal(5.99m, service.GetCurrentPrice("OP01-001", isFoil: false));
    }

    [Fact]
    public void GetCurrentPrice_UnknownId_ReturnsNull()
    {
        var dbFactory = new MockScryfallDbContextFactory(_scryfallOptions);
        var service = new ScryfallService(
            new MockHttpClientFactory(new MockNoOpHandler()),
            dbFactory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            new SetSymbolCache(new MockHttpClientFactory(new MockNoOpHandler()), new DataPathService(Path.GetTempPath()), NullLogger<SetSymbolCache>.Instance),
            Options.Create(new ScryfallSettings()),
            NullLogger<ScryfallService>.Instance,
            new DataPathService(Path.GetTempPath()));

        Assert.Null(service.GetCurrentPrice(Guid.NewGuid().ToString(), isFoil: false));
    }

    // --- Test helpers ---

    private class MockNoOpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    private class MockHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private class MockScryfallDbContextFactory(DbContextOptions<ScryfallDbContext> options) : IDbContextFactory<ScryfallDbContext>
    {
        public ScryfallDbContext CreateDbContext() => new(options);
    }

    private class MockOptcgDbContextFactory(DbContextOptions<OptcgDbContext> options) : IDbContextFactory<OptcgDbContext>
    {
        public OptcgDbContext CreateDbContext() => new(options);
    }
}
