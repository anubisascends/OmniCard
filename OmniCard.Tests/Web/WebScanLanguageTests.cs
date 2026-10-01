using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Imaging;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Scanning;
using OmniCard.Shared.Sets;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>
/// The scan's copy language: printed marker (MTG "• JP", Yu-Gi-Oh! "-DE") beats the session's chosen
/// language, which beats the matched row's own language; and a multi-language catalog remaps the match
/// to that language's printing when it holds one.
/// </summary>
public class WebScanLanguageTests
{
    private static byte[] PortraitPng()
    {
        using var bmp = new Bitmap(320, 440);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static WebScanMatchingService Build(FakeOcr ocr, ICardGameService game) =>
        new(new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            ocr, [game], NullLogger<WebScanMatchingService>.Instance);

    [Fact]
    public async Task Mtg_PrintedLanguageMarker_RemapsToThatPrinting()
    {
        var ocr = new FakeOcr { MtgLanguage = "ja" };
        var game = new FakeMultiLanguageGame();

        var dto = await Build(ocr, game).MatchAsync(PortraitPng(), CardGame.Mtg, isFoil: false);

        Assert.Equal("ja-id", dto.GameCardId);
        Assert.Equal("ja", dto.Language);
        Assert.True(dto.LanguageDetected);
        Assert.Equal("ja", game.LastOcrLanguage); // the printed language also steers the Phase-0 lookup
    }

    [Fact]
    public async Task Mtg_PrintedMarker_BeatsSessionLanguage()
    {
        var ocr = new FakeOcr { MtgLanguage = "en" };

        var dto = await Build(ocr, new FakeMultiLanguageGame()).MatchAsync(PortraitPng(), CardGame.Mtg, isFoil: false, language: "ja");

        Assert.Equal("en-id", dto.GameCardId);
        Assert.Equal("en", dto.Language);
        Assert.True(dto.LanguageDetected);
    }

    [Fact]
    public async Task SessionLanguage_RemapsWhenNothingPrinted()
    {
        var dto = await Build(new FakeOcr(), new FakeMultiLanguageGame()).MatchAsync(PortraitPng(), CardGame.Mtg, isFoil: false, language: "ja");

        Assert.Equal("ja-id", dto.GameCardId);
        Assert.Equal("ja", dto.Language);
        Assert.False(dto.LanguageDetected);
    }

    [Fact]
    public async Task NoLanguageAnywhere_StaysEnglish()
    {
        var dto = await Build(new FakeOcr(), new FakeMultiLanguageGame()).MatchAsync(PortraitPng(), CardGame.Mtg, isFoil: false);

        Assert.Equal("en-id", dto.GameCardId);
        Assert.Equal("en", dto.Language);
    }

    [Fact]
    public async Task NoLanguageChosen_ImageMatchedForeignRow_KeepsItsLanguage()
    {
        var game = new FakeMultiLanguageGame { PHashReturnsJapanese = true };

        var dto = await Build(new FakeOcr { NoMtgRead = true }, game).MatchAsync(PortraitPng(), CardGame.Mtg, isFoil: false);

        Assert.Equal("ja-id", dto.GameCardId);
        Assert.Equal("ja", dto.Language);
        Assert.False(dto.LanguageDetected);
    }

    [Fact]
    public async Task SessionEnglish_RemapsImageMatchedForeignRow()
    {
        var game = new FakeMultiLanguageGame { PHashReturnsJapanese = true };

        var dto = await Build(new FakeOcr { NoMtgRead = true }, game).MatchAsync(PortraitPng(), CardGame.Mtg, isFoil: false, language: "en");

        Assert.Equal("en-id", dto.GameCardId);
        Assert.Equal("en", dto.Language);
    }

    [Fact]
    public async Task SessionLanguage_WithoutCatalogRow_KeepsMatchAndTagsLanguage()
    {
        var dto = await Build(new FakeOcr(), new FakeMultiLanguageGame()).MatchAsync(PortraitPng(), CardGame.Mtg, isFoil: false, language: "de");

        Assert.Equal("en-id", dto.GameCardId);
        Assert.Equal("de", dto.Language);
    }

    [Fact]
    public async Task YuGiOh_RegionCode_SwapsToEnglishCatalogAndTagsLanguage()
    {
        var ocr = new FakeOcr { CollectorNumber = "RA05-DE085" };
        var game = new FakeYugiohGame();

        var dto = await Build(ocr, game).MatchAsync(PortraitPng(), CardGame.YuGiOh, isFoil: false);

        Assert.Equal("RA05-EN085", game.LastCollectorNumber);
        Assert.Equal("ygo-id", dto.GameCardId);
        Assert.Equal("de", dto.Language);
        Assert.True(dto.LanguageDetected);
    }

    private sealed class FakeOcr : IOcrMatchingService
    {
        public string? MtgLanguage;
        public bool NoMtgRead;
        public string? CollectorNumber;
        public Dictionary<string, ulong> SymbolHashes { get; set; } = [];
        public Task<OcrMatchResult> AnalyzeCardAsync(byte[] d) => Task.FromResult(new OcrMatchResult());
        public (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] d) => ([], 0);
        public Task<(string? CollectorNumber, double Confidence)> DetectOptcgCollectorNumberAsync(byte[] d) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? CollectorNumber, double Confidence)> DetectRiftboundCollectorNumberAsync(byte[] d) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? CollectorNumber, double Confidence)> DetectCollectorNumberAsync(byte[] d, OcrCollectorSpec s) =>
            Task.FromResult<(string?, double)>((CollectorNumber, CollectorNumber is null ? 0 : 0.9));
        public Task<(string? SetCode, string? CollectorNumber, double Confidence)> DetectMtgSetAndNumberAsync(byte[] d) =>
            Task.FromResult<(string?, string?, double)>(("NEO", "1", 0.96));
        public Task<(IReadOnlyList<MtgPrintedIdentity> Reads, double Confidence)> DetectMtgSetAndNumberCandidatesAsync(byte[] d) =>
            Task.FromResult<(IReadOnlyList<MtgPrintedIdentity>, double)>(NoMtgRead
                ? ([], 0)
                : ([new MtgPrintedIdentity("NEO", "1", 3, MtgLanguage)], 0.96));
    }

    /// <summary>An MTG-like catalog holding an English and a Japanese row of NEO #1.</summary>
    private sealed class FakeMultiLanguageGame : FakeGameBase, ICatalogLanguageAware
    {
        public bool PHashReturnsJapanese;
        public string? LastOcrLanguage;
        public override CardGame Game => CardGame.Mtg;

        private static CardMatch Row(string lang) => new()
        {
            Name = "Ancestral Recall", SetCode = "NEO", CollectorNumber = "1",
            GameSpecificId = $"{lang}-id", Language = lang, Confidence = 90,
        };

        public override CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null,
            IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null)
        {
            if (ocrResult?.SetCode is not null)
            {
                LastOcrLanguage = ocrResult.Language;
                return Row(ocrResult.Language ?? "en");
            }
            return Row(PHashReturnsJapanese ? "ja" : "en");
        }

        public IReadOnlyList<string> DownloadableLanguages => ["en", "ja"];
        public IReadOnlyCollection<string> CatalogLanguages { get; set; } = ["en", "ja"];
        public string? GetCardLanguage(string gameCardId) => gameCardId[..2];
        public CardMatch? FindLanguageVariant(string gameCardId, string language) =>
            language is "en" or "ja" && !gameCardId.StartsWith(language) ? Row(language) : null;
    }

    private sealed class FakeYugiohGame : FakeGameBase
    {
        public string? LastCollectorNumber;
        public override CardGame Game => CardGame.YuGiOh;

        public override CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null,
            IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null)
        {
            if (ocrResult?.CollectorNumber is null) return null;
            LastCollectorNumber = ocrResult.CollectorNumber;
            return new CardMatch { Name = "Blue-Eyes", SetCode = "RA05", CollectorNumber = ocrResult.CollectorNumber, GameSpecificId = "ygo-id" };
        }
    }

    private abstract class FakeGameBase : ICardGameService
    {
        public abstract CardGame Game { get; }
        public MatchDiagnostics? LastMatchDiagnostics => null;
        public abstract CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null,
            IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null);
        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => 1m;
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => gameCardIds.ToDictionary(i => i, _ => 1m);
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => [];
        public List<CardMatch> GetPrintings(string cardName) => [];
        public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
        public IReadOnlyList<SetInfo> GetAvailableSets() => [];
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
        public List<SetCatalogCard> GetSetCards(string setCode) => [];
        public object? FindCardById(string gameCardId) => null;
    }
}
