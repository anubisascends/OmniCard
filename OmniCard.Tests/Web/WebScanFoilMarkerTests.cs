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
/// The finish printed on a modern MTG card (★ foil / • non-foil) overrides the scan's foil setting: the
/// DTO reports it and the market price is looked up for that finish.
/// </summary>
public class WebScanFoilMarkerTests
{
    private const decimal FoilPrice = 9m, NonFoilPrice = 1m;

    private static byte[] PortraitPng()
    {
        using var bmp = new Bitmap(320, 440);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static Task<OmniCard.Api.Contracts.ScanMatchDto> Match(bool? printed, bool isFoil) =>
        new WebScanMatchingService(new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
                new FakeOcr { Foil = printed }, [new FakeGame()], NullLogger<WebScanMatchingService>.Instance)
            .MatchAsync(PortraitPng(), CardGame.Mtg, isFoil);

    [Fact]
    public async Task Star_OverridesNonFoilSetting()
    {
        var dto = await Match(printed: true, isFoil: false);
        Assert.True(dto.FoilDetected);
        Assert.Equal(FoilPrice, dto.MarketPrice);
    }

    [Fact]
    public async Task Bullet_OverridesFoilSetting()
    {
        var dto = await Match(printed: false, isFoil: true);
        Assert.False(dto.FoilDetected);
        Assert.Equal(NonFoilPrice, dto.MarketPrice);
    }

    [Fact]
    public async Task NoMarker_KeepsTheScanSetting()
    {
        var dto = await Match(printed: null, isFoil: true);
        Assert.Null(dto.FoilDetected);
        Assert.Equal(FoilPrice, dto.MarketPrice);
    }

    // --- fakes ---

    private sealed class FakeOcr : IOcrMatchingService
    {
        public bool? Foil;
        public Dictionary<string, ulong> SymbolHashes { get; set; } = [];
        public Task<OcrMatchResult> AnalyzeCardAsync(byte[] d) => Task.FromResult(new OcrMatchResult());
        public (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] d) => ([], 0);
        public Task<(string? CollectorNumber, double Confidence)> DetectOptcgCollectorNumberAsync(byte[] d) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? CollectorNumber, double Confidence)> DetectRiftboundCollectorNumberAsync(byte[] d) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? CollectorNumber, double Confidence)> DetectCollectorNumberAsync(byte[] d, OcrCollectorSpec s) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? SetCode, string? CollectorNumber, double Confidence)> DetectMtgSetAndNumberAsync(byte[] d) => Task.FromResult<(string?, string?, double)>(("MID", "365", 0.96));
        public Task<bool?> DetectMtgFoilMarkerAsync(byte[] d) => Task.FromResult(Foil);
    }

    private sealed class FakeGame : ICardGameService
    {
        public CardGame Game => CardGame.Mtg;
        public MatchDiagnostics? LastMatchDiagnostics => null;

        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null,
            IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) =>
            new() { Name = "Card", SetCode = "MID", CollectorNumber = "365", GameSpecificId = "mid-365" };

        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => isFoil ? FoilPrice : NonFoilPrice;
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) =>
            gameCardIds.ToDictionary(i => i, _ => isFoil ? FoilPrice : NonFoilPrice);

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
