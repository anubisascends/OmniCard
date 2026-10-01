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
/// One Piece scans hand the raw collector-line reads to the game service (OcrMatchResult.CollectorTexts).
/// When the read decided the match it stands — with its own confidence, not the pHash guess's — and when no
/// read resolved, the step-5 pHash match is kept.
/// </summary>
public class WebScanOptcgOcrTests
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
    public async Task ResolvedRead_IsGroundTruth()
    {
        var game = new FakeGame { ResolvesRead = true };

        var dto = await Build(new FakeOcr { Texts = ["OPO9-110"] }, game).MatchAsync(PortraitPng(), CardGame.OnePiece, isFoil: false);

        Assert.Equal("OP09-110", dto.GameCardId);
        Assert.Equal(100, dto.Confidence);
        Assert.Equal(["OPO9-110"], game.LastTexts);
    }

    [Fact]
    public async Task UnresolvedRead_KeepsThePHashMatch()
    {
        var game = new FakeGame { ResolvesRead = false };

        var dto = await Build(new FakeOcr { Texts = ["KP"] }, game).MatchAsync(PortraitPng(), CardGame.OnePiece, isFoil: false);

        Assert.Equal("phash-id", dto.GameCardId);
    }

    private sealed class FakeOcr : IOcrMatchingService
    {
        public IReadOnlyList<string> Texts = [];
        public Dictionary<string, ulong> SymbolHashes { get; set; } = [];
        public Task<IReadOnlyList<string>> ReadOptcgCollectorTextsAsync(byte[] d) => Task.FromResult(Texts);
        public Task<OcrMatchResult> AnalyzeCardAsync(byte[] d) => Task.FromResult(new OcrMatchResult());
        public (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] d) => ([], 0);
        public Task<(string? CollectorNumber, double Confidence)> DetectOptcgCollectorNumberAsync(byte[] d) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? CollectorNumber, double Confidence)> DetectRiftboundCollectorNumberAsync(byte[] d) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? CollectorNumber, double Confidence)> DetectCollectorNumberAsync(byte[] d, OcrCollectorSpec s) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? SetCode, string? CollectorNumber, double Confidence)> DetectMtgSetAndNumberAsync(byte[] d) => Task.FromResult<(string?, string?, double)>((null, null, 0));
    }

    private sealed class FakeGame : ICardGameService
    {
        public bool ResolvesRead;
        public IReadOnlyList<string>? LastTexts;
        public CardGame Game => CardGame.OnePiece;
        public MatchDiagnostics? LastMatchDiagnostics { get; private set; }

        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null,
            IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null)
        {
            if (ocrResult is not null) LastTexts = ocrResult.CollectorTexts;
            if (ocrResult is not null && ResolvesRead)
            {
                LastMatchDiagnostics = new MatchDiagnostics { DecisionPhase = "OcrCollectorNumber" };
                return new CardMatch { Name = "Pierre", SetCode = "OP09", CollectorNumber = "OP09-110", GameSpecificId = "OP09-110", Confidence = 100 };
            }
            // No read resolved (or no read at all): the same pHash fall-through every time.
            LastMatchDiagnostics = new MatchDiagnostics { DecisionPhase = "PHashConfident" };
            return new CardMatch { Name = "pHash pick", SetCode = "OP06", CollectorNumber = "OP06-019", GameSpecificId = "phash-id", Confidence = 29 };
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
