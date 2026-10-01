using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.CardMatching;
using OmniCard.CardMatching.Games;
using OmniCard.Data;
using OmniCard.Data.Catalogs;
using OmniCard.Imaging;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Matching;

namespace OmniCard.Tests.Services.Games;

/// <summary>Multi-language MTG catalog: all_cards download + prune, language-aware OCR lookup,
/// language variants and English price fallback.</summary>
public class ScryfallLanguageTests : IDisposable
{
    private const string DefaultCardsUri = "https://data.scryfall.io/default-cards/test.jsonl.gz";
    private const string AllCardsUri = "https://data.scryfall.io/all-cards/test.jsonl.gz";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ScryfallDbContext> _dbOptions;
    private readonly List<string> _requested = [];

    public ScryfallLanguageTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbOptions = new DbContextOptionsBuilder<ScryfallDbContext>().UseSqlite(_connection).Options;
        using var ctx = new ScryfallDbContext(_dbOptions);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private static readonly Guid EnglishId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid JapaneseId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Illustration = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static string CardLine(Guid id, string lang, string? usd, string imageStatus = "highres_scan") =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["oracle_id"] = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            ["name"] = "Ancestral Recall",
            ["lang"] = lang,
            ["type_line"] = "Instant",
            ["color_identity"] = Array.Empty<string>(),
            ["keywords"] = Array.Empty<string>(),
            ["legalities"] = new Dictionary<string, string>(),
            ["games"] = new[] { "paper" },
            ["finishes"] = new[] { "nonfoil" },
            ["nonfoil"] = true,
            ["set"] = "neo",
            ["set_name"] = "Kamigawa: Neon Dynasty",
            ["collector_number"] = "1",
            ["rarity"] = "rare",
            ["illustration_id"] = Illustration,
            ["image_status"] = imageStatus,
            ["image_uris"] = new Dictionary<string, string> { ["normal"] = $"https://img.example/{lang}.jpg" },
            ["prices"] = new Dictionary<string, string?> { ["usd"] = usd },
            ["related_uris"] = new Dictionary<string, string>(),
            ["purchase_uris"] = new Dictionary<string, string>(),
        });

    private ScryfallService CreateService(Dictionary<string, string>? responses = null)
    {
        var http = new MockHttpClientFactory(new MockHttpMessageHandler(responses ?? [], _requested));
        return new ScryfallService(
            http,
            new MockDbContextFactory(_dbOptions),
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            new SetSymbolCache(http, new DataPathService(Path.GetTempPath()), NullLogger<SetSymbolCache>.Instance),
            Options.Create(new ScryfallSettings()),
            NullLogger<ScryfallService>.Instance,
            new DataPathService(Path.GetTempPath()));
    }

    private static Dictionary<string, string> BulkResponses(string defaultLines, string allLines) => new()
    {
        ["https://api.scryfall.com/bulk-data/default_cards"] = JsonSerializer.Serialize(new { jsonl_download_uri = DefaultCardsUri }),
        ["https://api.scryfall.com/bulk-data/all_cards"] = JsonSerializer.Serialize(new { jsonl_download_uri = AllCardsUri }),
        [DefaultCardsUri] = defaultLines,
        [AllCardsUri] = allLines,
    };

    private void SeedEnglishAndJapanese(string japaneseImageStatus = "lowres")
    {
        using var ctx = new ScryfallDbContext(_dbOptions);
        ctx.Cards.AddRange(
            new Card
            {
                Id = EnglishId, Name = "Ancestral Recall", Lang = "en", SetCode = "neo", SetName = "Kamigawa",
                CollectorNumber = "1", Rarity = "rare", ImageStatus = "highres_scan",
                ImageUris = new ImageUris { Normal = "https://img.example/en.jpg" },
                Prices = new Prices { Usd = "4.00", UsdFoil = "9.00" },
            },
            new Card
            {
                Id = JapaneseId, Name = "Ancestral Recall", Lang = "ja", SetCode = "neo", SetName = "Kamigawa",
                CollectorNumber = "1", Rarity = "rare", ImageStatus = japaneseImageStatus,
                ImageUris = new ImageUris { Normal = "https://img.example/ja.jpg" },
                Prices = new Prices(),
            });
        ctx.SaveChanges();
    }

    [Fact]
    public async Task Download_EnglishOnly_UsesDefaultCardsAndSkipsOtherLanguages()
    {
        var both = $"{CardLine(EnglishId, "en", "4.00")}\n{CardLine(JapaneseId, "ja", null)}";
        var svc = CreateService(BulkResponses(both, both));

        await svc.DownloadBulkDataAsync();

        Assert.Contains("https://api.scryfall.com/bulk-data/default_cards", _requested);
        Assert.DoesNotContain("https://api.scryfall.com/bulk-data/all_cards", _requested);
        using var verify = new ScryfallDbContext(_dbOptions);
        Assert.Equal(["en"], verify.Cards.Select(c => c.Lang).ToList());
    }

    [Fact]
    public async Task Download_WithJapanese_UsesAllCards_ThenUntickingPrunesJapaneseOnly()
    {
        var english = CardLine(EnglishId, "en", "4.00");
        var both = $"{english}\n{CardLine(JapaneseId, "ja", null)}";
        var svc = CreateService(BulkResponses(english, both));

        svc.CatalogLanguages = ["en", "ja"];
        await svc.DownloadBulkDataAsync();
        Assert.Contains("https://api.scryfall.com/bulk-data/all_cards", _requested);
        using (var verify = new ScryfallDbContext(_dbOptions))
            Assert.Equal(["en", "ja"], verify.Cards.Select(c => c.Lang).OrderBy(l => l).ToList());

        svc.CatalogLanguages = ["en"];
        await svc.DownloadBulkDataAsync();
        using (var verify = new ScryfallDbContext(_dbOptions))
            Assert.Equal([EnglishId], verify.Cards.Select(c => c.Id).ToList());
    }

    [Fact]
    public void CatalogLanguages_AlwaysKeepsEnglish_AndDropsUnsupported()
    {
        var svc = CreateService();
        svc.CatalogLanguages = ["jp", "klingon"];
        Assert.Equal(["en", "ja"], svc.CatalogLanguages);
    }

    [Fact]
    public void GetCurrentPrices_JapaneseWithoutPrice_UsesEnglishPrinting()
    {
        SeedEnglishAndJapanese();
        var svc = CreateService();

        Assert.Equal(4.00m, svc.GetCurrentPrice(JapaneseId.ToString(), isFoil: false));
        Assert.Equal(9.00m, svc.GetCurrentPrices([JapaneseId.ToString()], isFoil: true)[JapaneseId.ToString()]);
        Assert.Equal(4.00m, svc.GetCurrentPrice(EnglishId.ToString(), isFoil: false));
    }

    [Fact]
    public void FindLanguageVariant_ReturnsSamePrintingInLanguage()
    {
        SeedEnglishAndJapanese();
        var svc = CreateService();

        var ja = svc.FindLanguageVariant(EnglishId.ToString(), "ja");
        Assert.NotNull(ja);
        Assert.Equal(JapaneseId.ToString(), ja.GameSpecificId);
        Assert.Equal("ja", ja.Language);
        Assert.Equal("https://img.example/ja.jpg", ja.ImageUri);

        Assert.Equal(EnglishId.ToString(), svc.FindLanguageVariant(JapaneseId.ToString(), "en")?.GameSpecificId);
        Assert.Null(svc.FindLanguageVariant(EnglishId.ToString(), "de"));
        Assert.Equal("ja", svc.GetCardLanguage(JapaneseId.ToString()));
    }

    [Fact]
    public void FindLanguageVariant_PlaceholderScan_BorrowsEnglishArt()
    {
        SeedEnglishAndJapanese(japaneseImageStatus: "placeholder");
        var svc = CreateService();

        var ja = svc.FindLanguageVariant(EnglishId.ToString(), "ja");
        Assert.Equal("https://img.example/en.jpg", ja?.ImageUri);
    }

    [Theory]
    [InlineData(null, "11111111-1111-1111-1111-111111111111")]
    [InlineData("en", "11111111-1111-1111-1111-111111111111")]
    [InlineData("ja", "22222222-2222-2222-2222-222222222222")]
    public void FindClosestMatch_OcrSetCollector_PicksPrintedLanguage(string? ocrLanguage, string expectedId)
    {
        SeedEnglishAndJapanese();
        var svc = CreateService();

        var match = svc.FindClosestMatch(0, ocrResult: new OcrMatchResult
        {
            SetCode = "NEO", CollectorNumber = "1", CollectorNumberConfidence = 0.95, Language = ocrLanguage,
        });

        Assert.Equal(expectedId, match?.GameSpecificId);
    }

    [Fact]
    public void GetSetCards_ListsEnglishRowPerCollectorNumber()
    {
        SeedEnglishAndJapanese();
        var svc = CreateService();

        var card = Assert.Single(svc.GetSetCards("neo"));
        Assert.Equal(EnglishId.ToString(), card.GameCardId);
    }

    // --- Test helpers ---

    private sealed class MockHttpMessageHandler(Dictionary<string, string> responses, List<string> requested) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            lock (requested) requested.Add(url);
            if (!responses.TryGetValue(url, out var body))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            HttpContent content;
            if (url.EndsWith(".gz"))
            {
                var ms = new MemoryStream();
                using (var gzip = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
                using (var writer = new StreamWriter(gzip))
                    writer.Write(body);
                content = new ByteArrayContent(ms.ToArray());
            }
            else
            {
                content = new StringContent(body, Encoding.UTF8, "application/json");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class MockHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class MockDbContextFactory(DbContextOptions<ScryfallDbContext> options) : IDbContextFactory<ScryfallDbContext>
    {
        public ScryfallDbContext CreateDbContext() => new(options);
    }
}
