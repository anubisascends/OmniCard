using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Helpers;
using OmniCard.Imaging;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class ScryfallServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ScryfallDbContext> _dbOptions;

    public ScryfallServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbOptions = new DbContextOptionsBuilder<ScryfallDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new ScryfallDbContext(_dbOptions);
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public void FlattenFrontFace_CopiesFrontFaceFieldsToCard()
    {
        var card = new Card
        {
            Id = Guid.NewGuid(),
            Name = "Delver of Secrets // Insectile Aberration",
            CardFaces =
            [
                new CardFace
                {
                    Name = "Delver of Secrets",
                    ManaCost = "{U}",
                    OracleText = "At the beginning of your upkeep...",
                    Colors = ["U"],
                    Power = "1",
                    Toughness = "1",
                    Artist = "Matt Stewart",
                    ArtistId = Guid.Parse("18015838-148d-4ba7-9ea4-7a1348263e31"),
                    IllustrationId = Guid.NewGuid(),
                    ImageUris = new ImageUris
                    {
                        Normal = "https://cards.scryfall.io/normal/delver-front.jpg"
                    }
                },
                new CardFace
                {
                    Name = "Insectile Aberration",
                    ManaCost = "",
                    OracleText = "Flying",
                    Colors = ["U"],
                    Power = "3",
                    Toughness = "2"
                }
            ]
        };

        ScryfallService.FlattenFrontFace(card);

        Assert.Equal("{U}", card.ManaCost);
        Assert.Equal("At the beginning of your upkeep...", card.OracleText);
        Assert.Equal(["U"], card.Colors);
        Assert.Equal("1", card.Power);
        Assert.Equal("1", card.Toughness);
        Assert.Equal("Matt Stewart", card.Artist);
        Assert.NotNull(card.ArtistIds);
        Assert.Equal(Guid.Parse("18015838-148d-4ba7-9ea4-7a1348263e31"), card.ArtistIds[0]);
        Assert.NotNull(card.ImageUris);
        Assert.Equal("https://cards.scryfall.io/normal/delver-front.jpg", card.ImageUris.Normal);
        Assert.Null(card.CardFaces);
    }

    [Fact]
    public void FlattenFrontFace_DoesNotOverwriteExistingTopLevelFields()
    {
        var card = new Card
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            ManaCost = "{R}",
            OracleText = "Top-level text",
            CardFaces =
            [
                new CardFace
                {
                    Name = "Front",
                    ManaCost = "{U}",
                    OracleText = "Face text"
                }
            ]
        };

        ScryfallService.FlattenFrontFace(card);

        Assert.Equal("{R}", card.ManaCost);
        Assert.Equal("Top-level text", card.OracleText);
    }

    [Fact]
    public void FlattenFrontFace_NoCardFaces_DoesNothing()
    {
        var card = new Card
        {
            Id = Guid.NewGuid(),
            Name = "Normal Card",
            ManaCost = "{W}"
        };

        ScryfallService.FlattenFrontFace(card);

        Assert.Equal("{W}", card.ManaCost);
        Assert.Null(card.CardFaces);
    }

    [Fact]
    public void MapAllParts_ConvertsEntriesToRelatedCards()
    {
        var cardId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var card = new Card
        {
            Id = cardId,
            Name = "Brood Monitor",
            AllParts =
            [
                new AllPartsEntry
                {
                    Id = tokenId,
                    Component = "token",
                    Name = "Eldrazi Scion",
                    TypeLine = "Token Creature",
                    Uri = "https://api.scryfall.com/cards/token"
                }
            ]
        };

        ScryfallService.MapAllParts(card);

        Assert.Single(card.RelatedCards);
        Assert.Equal(cardId, card.RelatedCards[0].CardId);
        Assert.Equal(tokenId, card.RelatedCards[0].ScryfallId);
        Assert.Equal("token", card.RelatedCards[0].Component);
        Assert.Equal("Eldrazi Scion", card.RelatedCards[0].Name);
        Assert.Null(card.AllParts);
    }

    [Fact]
    public async Task DownloadBulkDataAsync_ImportsCardsToDatabase()
    {
        // Arrange: mock HTTP responses
        var bulkDataJson = JsonSerializer.Serialize(new
        {
            download_uri = "https://data.scryfall.io/default-cards/test.json"
        });

        var cardsJson = """
        [
            {
                "id": "0000579f-7b35-4ed3-b44c-db2a538066fe",
                "oracle_id": "44623693-51d6-49ad-8cd7-140505caf02f",
                "name": "Fury Sliver",
                "lang": "en",
                "released_at": "2006-10-06",
                "uri": "https://api.scryfall.com/cards/0000579f",
                "scryfall_uri": "https://scryfall.com/card/tsp/157",
                "layout": "normal",
                "highres_image": true,
                "image_status": "highres_scan",
                "mana_cost": "{5}{R}",
                "cmc": 6.0,
                "type_line": "Creature",
                "colors": ["R"],
                "color_identity": ["R"],
                "keywords": [],
                "legalities": {"modern": "legal"},
                "games": ["paper"],
                "reserved": false,
                "game_changer": false,
                "foil": false,
                "nonfoil": true,
                "finishes": ["nonfoil"],
                "oversized": false,
                "promo": false,
                "reprint": false,
                "variation": false,
                "set_id": "c1d109bc-f5c0-4d3f-9fea-7102bf36afed",
                "set": "tsp",
                "set_name": "Time Spiral",
                "set_type": "expansion",
                "set_uri": "https://api.scryfall.com/sets/test",
                "set_search_uri": "https://api.scryfall.com/cards/search?q=test",
                "scryfall_set_uri": "https://scryfall.com/sets/tsp",
                "rulings_uri": "https://api.scryfall.com/cards/test/rulings",
                "prints_search_uri": "https://api.scryfall.com/cards/search?q=test",
                "collector_number": "157",
                "digital": false,
                "rarity": "uncommon",
                "border_color": "black",
                "frame": "2003",
                "full_art": false,
                "textless": false,
                "booster": true,
                "story_spotlight": false,
                "prices": {"usd": "0.35", "usd_foil": null, "usd_etched": null, "eur": null, "eur_foil": null, "tix": null},
                "related_uris": {},
                "purchase_uris": {}
            }
        ]
        """;

        var handler = new MockHttpMessageHandler(new Dictionary<string, string>
        {
            ["https://api.scryfall.com/bulk-data/default_cards"] = bulkDataJson,
            ["https://data.scryfall.io/default-cards/test.json"] = cardsJson
        });

        var httpClientFactory = new MockHttpClientFactory(handler);
        var dbContextFactory = new MockDbContextFactory(_dbOptions);

        var service = new ScryfallService(httpClientFactory, dbContextFactory, new PerceptualHashService(NullLogger<PerceptualHashService>.Instance), new SetSymbolCache(httpClientFactory, new DataPathService(Path.GetTempPath()), NullLogger<SetSymbolCache>.Instance), Options.Create(new ScryfallSettings()), NullLogger<ScryfallService>.Instance, new DataPathService(Path.GetTempPath()));
        var progress = new List<string>();

        // Act
        await service.DownloadBulkDataAsync(new Progress<string>(p => progress.Add(p)));

        // Assert
        using var verifyContext = new ScryfallDbContext(_dbOptions);
        var cards = verifyContext.Cards.ToList();
        Assert.Single(cards);
        Assert.Equal("Fury Sliver", cards[0].Name);
        Assert.Equal("tsp", cards[0].SetCode);
    }

    private ScryfallService CreateServiceWithLanguages(params string[] languages)
    {
        var settings = Options.Create(new ScryfallSettings { Languages = languages.ToList() });
        var httpFactory = new MockHttpClientFactory(new MockHttpMessageHandler(new Dictionary<string, string>()));
        return new ScryfallService(
            httpFactory,
            new MockDbContextFactory(_dbOptions),
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            new SetSymbolCache(httpFactory, new DataPathService(Path.GetTempPath()), NullLogger<SetSymbolCache>.Instance),
            settings,
            NullLogger<ScryfallService>.Instance,
            new DataPathService(Path.GetTempPath()));
    }

    [Fact]
    public void Constructor_AcceptsScryfallSettings()
    {
        var svc = CreateServiceWithLanguages("en", "ja");
        Assert.NotNull(svc);
    }

    // --- Test helpers ---

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses;

        public MockHttpMessageHandler(Dictionary<string, string> responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            if (_responses.TryGetValue(url, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private class MockHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public MockHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private class MockDbContextFactory : IDbContextFactory<ScryfallDbContext>
    {
        private readonly DbContextOptions<ScryfallDbContext> _options;
        public MockDbContextFactory(DbContextOptions<ScryfallDbContext> options) => _options = options;
        public ScryfallDbContext CreateDbContext() => new ScryfallDbContext(_options);
    }

}
