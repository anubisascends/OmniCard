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
/// A Cyberpunk TCG scan reads the lower-left corner (product code + number) and hands the game service the
/// number as the catalog stores it ("120", Beta "B120") with the printed set code ("WNC") alongside — never
/// the MTG set/number OCR the scan switch otherwise falls back to.
/// </summary>
public class WebScanCyberpunkOcrTests
{
    private static byte[] PortraitPng()
        => OmniCard.Tests.Fakes.TestImages.Png(320, 440, SkiaSharp.SKColors.White);

    private static WebScanMatchingService Build(FakeOcr ocr, FakeGame game) =>
        new(new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            ocr, [game], NullLogger<WebScanMatchingService>.Instance);

    [Theory]
    [InlineData(false, "120")]
    [InlineData(true, "B120")]
    public async Task Read_PassesCatalogNumberAndSetCode(bool beta, string catalogNumber)
    {
        var ocr = new FakeOcr { Code = new CyberpunkPrintedCode("MS01", "WNC", "A", "120", beta) };
        var game = new FakeGame();

        var dto = await Build(ocr, game).MatchAsync(PortraitPng(), CardGame.Cyberpunk, isFoil: false);

        Assert.Equal("ocr-id", dto.GameCardId);
        var read = Assert.Single(game.Reads);
        Assert.Equal(catalogNumber, read.CollectorNumber);
        Assert.Equal("WNC", read.SetCode);
        Assert.False(ocr.MtgCalled);
    }

    [Fact]
    public async Task NoRead_KeepsTheImageMatch()
    {
        var ocr = new FakeOcr { Code = CyberpunkPrintedCode.Empty };
        var game = new FakeGame();

        var dto = await Build(ocr, game).MatchAsync(PortraitPng(), CardGame.Cyberpunk, isFoil: false);

        Assert.Equal("phash-id", dto.GameCardId);
        Assert.Empty(game.Reads);
        Assert.False(ocr.MtgCalled);
    }

    private sealed class FakeOcr : IOcrMatchingService
    {
        public CyberpunkPrintedCode Code = CyberpunkPrintedCode.Empty;
        public bool MtgCalled;
        public Dictionary<string, ulong> SymbolHashes { get; set; } = [];
        public Task<OcrMatchResult> AnalyzeCardAsync(byte[] d) => Task.FromResult(new OcrMatchResult());
        public (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] d) => ([], 0);
        public Task<(string? CollectorNumber, double Confidence)> DetectOptcgCollectorNumberAsync(byte[] d) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? CollectorNumber, double Confidence)> DetectRiftboundCollectorNumberAsync(byte[] d) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? CollectorNumber, double Confidence)> DetectCollectorNumberAsync(byte[] d, OcrCollectorSpec s) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? SetCode, string? CollectorNumber, double Confidence)> DetectMtgSetAndNumberAsync(byte[] d)
        {
            MtgCalled = true;
            return Task.FromResult<(string?, string?, double)>((null, null, 0));
        }
        public Task<CyberpunkPrintedCode> ReadCyberpunkCodeAsync(byte[] d) => Task.FromResult(Code);
    }

    private sealed class FakeGame : ICardGameService
    {
        public List<OcrMatchResult> Reads { get; } = [];
        public CardGame Game => CardGame.Cyberpunk;
        public MatchDiagnostics? LastMatchDiagnostics => null;

        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null,
            IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null)
        {
            if (ocrResult?.CollectorNumber is null)
                return new CardMatch { Name = "pHash pick", SetCode = "24855", CollectorNumber = "006", GameSpecificId = "phash-id" };
            Reads.Add(ocrResult);
            return new CardMatch { Name = "Modded Kusanagi", SetCode = "24855", CollectorNumber = ocrResult.CollectorNumber, GameSpecificId = "ocr-id" };
        }

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
