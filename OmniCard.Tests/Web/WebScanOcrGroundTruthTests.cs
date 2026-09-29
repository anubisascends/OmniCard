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
/// The MTG (set, collector) OCR read is ground truth only when it actually names the printing returned.
/// When it doesn't resolve, FindClosestMatch falls through to plain pHash (and, for a confident read, with the
/// user's set filter lifted) — that fallback must not be returned as if the read had identified the card.
/// Regression: an old-frame credit line misread as "BSR 2" short-circuited the whole pipeline.
/// </summary>
public class WebScanOcrGroundTruthTests
{
    private static byte[] PortraitPng()
    {
        using var bmp = new Bitmap(320, 440);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static WebScanMatchingService Build(FakeOcr ocr, FakeGame game) =>
        new(new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            ocr, [game], NullLogger<WebScanMatchingService>.Instance);

    [Fact]
    public async Task UnresolvedRead_DoesNotShortCircuit_WithPHashFallback()
    {
        var ocr = new FakeOcr { Set = "BSR", Num = "2" };
        var game = new FakeGame { ResolvesRead = false };

        var dto = await Build(ocr, game).MatchAsync(PortraitPng(), CardGame.Mtg, isFoil: false);

        // The step-5 pHash match stands; the "ground truth" call's unfiltered pHash fallback is ignored.
        Assert.Equal("phash-id", dto.GameCardId);
    }

    [Fact]
    public async Task ResolvedRead_IsGroundTruth()
    {
        var ocr = new FakeOcr { Set = "MKC", Num = "0066" };
        var game = new FakeGame { ResolvesRead = true };

        var dto = await Build(ocr, game).MatchAsync(PortraitPng(), CardGame.Mtg, isFoil: false);

        Assert.Equal("gt-id", dto.GameCardId);
        Assert.Equal("MKC", dto.SetCode);
    }

    private sealed class FakeOcr : IOcrMatchingService
    {
        public string? Set;
        public string? Num;
        public Dictionary<string, ulong> SymbolHashes { get; set; } = [];
        public Task<OcrMatchResult> AnalyzeCardAsync(byte[] d) => Task.FromResult(new OcrMatchResult());
        public (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] d) => ([], 0);
        public Task<(string? CollectorNumber, double Confidence)> DetectOptcgCollectorNumberAsync(byte[] d) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? CollectorNumber, double Confidence)> DetectRiftboundCollectorNumberAsync(byte[] d) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? CollectorNumber, double Confidence)> DetectCollectorNumberAsync(byte[] d, OcrCollectorSpec s) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? SetCode, string? CollectorNumber, double Confidence)> DetectMtgSetAndNumberAsync(byte[] d) => Task.FromResult((Set, Num, 0.96));
    }

    private sealed class FakeGame : ICardGameService
    {
        public bool ResolvesRead;
        public CardGame Game => CardGame.Mtg;
        public MatchDiagnostics? LastMatchDiagnostics => null;

        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null,
            IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null)
        {
            if (ocrResult?.SetCode is null)
                return new CardMatch { Name = "pHash pick", SetCode = "6ed", CollectorNumber = "301", GameSpecificId = "phash-id" };
            // Phase 0 hit → the read's printing ("0066" is stored as "66"); miss → FindClosestMatch's pHash fall-through.
            return ResolvesRead
                ? new CardMatch { Name = "Read card", SetCode = ocrResult.SetCode, CollectorNumber = ocrResult.CollectorNumber!.TrimStart('0'), GameSpecificId = "gt-id" }
                : new CardMatch { Name = "Unfiltered pHash", SetCode = "sos", CollectorNumber = "253", GameSpecificId = "fallthrough-id" };
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
