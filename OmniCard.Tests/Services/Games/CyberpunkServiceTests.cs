using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.CardMatching.Games;
using OmniCard.Collection.Inventory;
using OmniCard.Data.Catalogs;
using OmniCard.Imaging;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Settings;

namespace OmniCard.Tests.Services.Games;

/// <summary>Cyberpunk TCG (TCGCSV category 92): download mapping with distinct codes for groups that share an
/// abbreviation, attribute normalization ("x2" RAM, "04" cost, "TRUE"/"true"), the game search fields
/// and the Normal/Foil price mapping. Shapes mirror the real TCGCSV data.</summary>
public class CyberpunkServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<CyberpunkDbContext> _factory;
    private readonly string _dataDir;

    public CyberpunkServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<CyberpunkDbContext>().UseSqlite(_connection).Options;
        _factory = new CyberpunkFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete();
        _dataDir = Path.Combine(Path.GetTempPath(), "cyberpunk-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    // Two groups share "PRR" (as on TCGCSV); the main set (Retail and Beta) has no abbreviation at all.
    private const string GroupsJson = """
    {"results":[
      {"groupId":24855,"name":"Welcome to Night City - Retail","abbreviation":""},
      {"groupId":24845,"name":"Welcome to Night City - Beta","abbreviation":""},
      {"groupId":24883,"name":"Edgerunner Open Season 1","abbreviation":"EOR01"},
      {"groupId":24928,"name":"Pre-Release Retail","abbreviation":"PRR"},
      {"groupId":24880,"name":"Pre-Release Beta","abbreviation":"PRR"}
    ],"success":true,"errors":[]}
    """;

    // The sample scans' cards (OmniCard.Tests/TestData/CyberpunkCode), in Retail, Beta (β numbers stored as
    // "B…") and — for Trust No One — the Edgerunner Open promo, real TCGCSV product ids.
    private static readonly (int Id, int Group, string Name, string Number)[] SampleCards =
    [
        (715045, 24855, "Hacked Corpo", "114"), (715077, 24855, "Modded Kusanagi", "120"),
        (715141, 24855, "Westbrook Netrunner", "127"), (715129, 24855, "Trust No One", "139"),
        (714276, 24845, "Hacked Corpo", "B114"), (714282, 24845, "Modded Kusanagi", "B120"),
        (714289, 24845, "Westbrook Netrunner", "B127"), (714301, 24845, "Trust No One", "B139"),
        (718544, 24883, "Trust No One", "007"),
        // Shares its bare number with the main set's 6th Street Recruits (715006).
        (719690, 24883, "Field Operator", "006"),
    ];

    private static string Ext(string name, string value) =>
        $$"""{"name":"{{name}}","displayName":"{{name}}","value":"{{value}}"}""";

    private static string Product(int id, int groupId, string name, params string[] ext) =>
        $$"""{"productId":{{id}},"name":"{{name}}","cleanName":"{{name}}","imageUrl":"https://cdn/{{id}}_200w.jpg","groupId":{{groupId}},"url":"https://tcg/{{id}}","extendedData":[{{string.Join(",", ext)}}]}""";

    private static string ProductsFor(string uri)
    {
        string[] products =
            uri.Contains("/24855/") ?
            [
                Product(715006, 24855, "6th Street Recruits", Ext("Number", "006"), Ext("Rarity", "C"), Ext("CardType", "Unit"),
                    Ext("Color", "Red"), Ext("Tags", "6th Street;Ganger"), Ext("Cost", "4"), Ext("Power", "6"), Ext("RAM", "x1"), Ext("Eddies", "FALSE")),
                Product(715005, 24855, "(Don't Fear) The Reaper", Ext("Number", "098"), Ext("Rarity", "R"), Ext("CardType", "Program"),
                    Ext("Color", "Green"), Ext("Tags", "Samurai"), Ext("Cost", "7"), Ext("RAM", "x3"), Ext("Eddies", "TRUE")),
            ]
            : uri.Contains("/24928/") ?
            [
                Product(800001, 24928, "Adam Smasher - Ender of Legends", Ext("Number", "001"), Ext("Rarity", "E"), Ext("CardType", "Legend"),
                    Ext("Color", "Red"), Ext("Tags", "Arasaka;Merc"), Ext("Cost", "09"), Ext("Power", "09"), Ext("RAM", "x2"), Ext("Eddies", "true")),
            ]
            : uri.Contains("/24880/") ?
            [
                Product(800002, 24880, "Adam Smasher - Ender of Legends", Ext("Number", "001"), Ext("Rarity", "E"), Ext("CardType", "Legend"),
                    Ext("Color", "Red"), Ext("Tags", "Arasaka;Merc"), Ext("Cost", "9"), Ext("Power", "9"), Ext("RAM", "x2"), Ext("Eddies", "TRUE")),
            ]
            : [];
        // The sample cards carry no RAM/cost/traits, so the attribute searches below don't see them.
        var samples = SampleCards.Where(s => uri.Contains($"/{s.Group}/"))
            .Select(s => Product(s.Id, s.Group, s.Name, Ext("Number", s.Number), Ext("Rarity", "C"),
                Ext("CardType", "Unit"), Ext("Color", "Blue")));
        return $$"""{"results":[{{string.Join(",", products.Concat(samples))}}],"success":true,"errors":[]}""";
    }

    private CyberpunkService CreateService()
    {
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        return new CyberpunkService(
            new CyberpunkHttpClientFactory(new CyberpunkRoutingHandler(uri =>
                uri.EndsWith("/groups") ? GroupsJson
                : uri.EndsWith("/products") ? ProductsFor(uri)
                : null)),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<CyberpunkService>.Instance);
    }

    private async Task<CyberpunkService> DownloadedService()
    {
        var svc = CreateService();
        await svc.DownloadBulkDataAsync();
        return svc;
    }

    [Fact]
    public async Task Download_SharedAbbreviation_GetsDistinctSetCodes_BlankUsesGroupId()
    {
        await DownloadedService();

        using var ctx = _factory.CreateDbContext();
        var codes = ctx.Cards.ToDictionary(c => c.ProductId, c => c.SetCode);
        Assert.Equal("24855", codes[715006]);
        Assert.Equal("PRR-24928", codes[800001]);
        Assert.Equal("PRR-24880", codes[800002]);
        Assert.All(ctx.Cards, c => Assert.Equal(CardGame.Cyberpunk, c.Game));
    }

    [Fact]
    public async Task Download_MapsNumberRarityType_AndNormalizesAttributes()
    {
        await DownloadedService();

        using var ctx = _factory.CreateDbContext();
        var adam = ctx.Cards.Single(c => c.ProductId == 800001);
        Assert.Equal("001", adam.CollectorNumber);
        Assert.Equal("E", adam.Rarity);
        Assert.Equal("Legend", adam.CardType);

        var attrs = ExtendedDataParser.ParseToLookup(adam.ExtendedDataJson);
        Assert.Equal("2", attrs["RAM"]);
        Assert.Equal("9", attrs["Cost"]);
        Assert.Equal("9", attrs["Power"]);
        Assert.Equal("True", attrs["Eddies"]);
        Assert.Equal("Arasaka;Merc", attrs["Tags"]);   // untouched
    }

    [Theory]
    [InlineData("c:r", new[] { 715006, 800001, 800002 })]
    [InlineData("color:green", new[] { 715005 })]
    [InlineData("ram<=2", new[] { 715006, 800001, 800002 })]
    [InlineData("ram>=3", new[] { 715005 })]
    [InlineData("cost=9", new[] { 800001, 800002 })]
    [InlineData("eddies:true", new[] { 715005, 800001, 800002 })]
    [InlineData("eddies:false", new[] { 715006 })]
    [InlineData("trait:ganger", new[] { 715006 })]
    [InlineData("set:prr-24928", new[] { 800001 })]
    public async Task Search_GameFields(string query, int[] expected)
    {
        var svc = await DownloadedService();
        var ids = svc.SearchCards(query).Select(m => int.Parse(m.GameSpecificId)).OrderBy(i => i);
        Assert.Equal(expected.OrderBy(i => i), ids);
    }

    [Fact]
    public async Task ExtractColor_ReadsColorAttribute()
    {
        var svc = await DownloadedService();
        var match = new CardMatch { Source = svc.FindCardById("715005") };
        Assert.Equal("Green", CardAttributeExtractor.ExtractColor(match, CardGame.Cyberpunk));
        Assert.Equal("Program", CardAttributeExtractor.ExtractCardType(match, CardGame.Cyberpunk));
    }

    [Fact]
    public void SubtypePrices_MapsNormalAndFoil()
    {
        var rows = new List<TcgCsvPrice>
        {
            new() { ProductId = 1, SubTypeName = "Normal", MarketPrice = 0.25m },
            new() { ProductId = 1, SubTypeName = "Foil", MarketPrice = 2.10m },
        };
        var (normal, foil) = CyberpunkService.MapSubtypePricesForTest(rows);
        Assert.Equal(0.25m, normal);
        Assert.Equal(2.10m, foil);
    }

    [Theory]
    [InlineData("RAM", "x2", "2")]
    [InlineData("RAM", "X4", "4")]
    [InlineData("Cost", "04", "4")]
    [InlineData("Cost", "0", "0")]
    [InlineData("Power", "00", "0")]
    [InlineData("Power", "6+", "6+")]
    [InlineData("Eddies", "FALSE", "False")]
    [InlineData("Tags", "Arasaka;Merc", "Arasaka;Merc")]
    public void NormalizeAttribute(string name, string value, string expected) =>
        Assert.Equal(expected, CyberpunkService.NormalizeAttribute(name, value));

    [Theory]
    [InlineData("Welcome to Night City - Retail", "WNC")]
    [InlineData("Welcome to Night City - Beta", "WNC")]
    [InlineData("Edgerunner Open Season 1", "EOS1")]
    [InlineData("Set 1 Promos", "S1P")]
    public void PrintedSetCode_IsTheNamesInitials(string setName, string code) =>
        Assert.Equal(code, CyberpunkService.PrintedSetCode(setName));

    private static OcrMatchResult Read(string number, string? setCode) =>
        new() { CollectorNumber = number, CollectorNumberConfidence = 0.9, SetCode = setCode };

    [Theory]
    [InlineData("120", "WNC", "715077")]    // Retail
    [InlineData("B120", "WNC", "714282")]   // Beta (β120)
    [InlineData("006", "WNC", "715006")]    // bare number shared with a promo: the set code decides
    [InlineData("006", "EOS1", "719690")]
    [InlineData("006", "WAC", "715006")]    // set code a letter off still narrows
    [InlineData("007", "WNC", "718544")]    // the only 007 — an unknown printed code doesn't block it
    public async Task FindClosestMatch_UsesNumberAndPrintedSetCode(string number, string setCode, string expectedId)
    {
        var svc = await DownloadedService();

        var match = svc.FindClosestMatch(0UL, ocrResult: Read(number, setCode));

        Assert.Equal(expectedId, match?.GameSpecificId);
        Assert.Equal("OcrCollectorNumber", svc.LastMatchDiagnostics?.DecisionPhase);
    }

    /// <summary>End to end on the sample scans: OCR the corner, then resolve it against the catalog — the
    /// Retail printing, not Beta's same-art copy or the Trust No One promo.</summary>
    [Theory]
    [InlineData("120-modded-kusanagi", "715077")]
    [InlineData("127-westbrook-netrunner-a", "715141")]
    [InlineData("127-westbrook-netrunner-b", "715141")]
    [InlineData("139-trust-no-one", "715129")]
    [InlineData("114-hacked-corpo", "715045")]
    public async Task SampleScans_ResolveToTheirRetailPrinting(string fixture, string expectedId)
    {
        var svc = await DownloadedService();
        using var scan = Matching.CyberpunkCodeOcrTests.ScanFromFixture(fixture);
        using var ocr = new OcrMatchingService(new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            NullLogger<OcrMatchingService>.Instance);

        var code = await ocr.ReadCyberpunkCodeAsync(ImageOps.EncodePng(scan));
        var match = svc.FindClosestMatch(0UL, ocrResult: Read(code.CatalogNumber!, code.SetCode));

        Assert.Equal(expectedId, match?.GameSpecificId);
    }

    [Fact]
    public void SetCodeFor_Unshared_KeepsAbbreviationOrGroupId()
    {
        Assert.Equal("OP1", TcgCsvGameService<CyberpunkDbContext>.SetCodeFor(new TcgCsvGroup { GroupId = 1, Abbreviation = "OP1" }, false));
        Assert.Equal("7", TcgCsvGameService<CyberpunkDbContext>.SetCodeFor(new TcgCsvGroup { GroupId = 7, Abbreviation = " " }, true));
    }

    [Fact]
    public void BuiltInDeckTypes_IncludeConstructed()
    {
        var constructed = DeckTypeService.BuiltIns.Single(d => d.BuiltInKey == "cyberpunk.constructed");
        Assert.Equal(CardGame.Cyberpunk, constructed.Game);
        Assert.Equal(3, constructed.CommanderSlots);
        Assert.Equal(3, constructed.MaxCopiesPerCard);
    }
}

file class CyberpunkRoutingHandler(Func<string, string?> route) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = route(request.RequestUri!.ToString());
        return Task.FromResult(body is null
            ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            : new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
    }
}

file class CyberpunkHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler);
}

file class CyberpunkFactory(DbContextOptions<CyberpunkDbContext> options) : IDbContextFactory<CyberpunkDbContext>
{
    public CyberpunkDbContext CreateDbContext() => new(options);
}
