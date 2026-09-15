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
/// The List (plst) remap in the MTG scan path: when the Planeswalker glyph is detected, the printed
/// original set/collector is remapped to the plst printing (keyed "{ORIGINALSET}-{collector}"), which is
/// the correct, cheaper identity. When plst isn't in the catalog the match falls back to the original and
/// the DTO is flagged so the reviewer can check the price. Drives the detection + flags with fakes so the
/// remap logic is exercised without a live catalog.
/// </summary>
public class WebScanListReprintTests
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
    public async Task GlyphDetected_RemapsToPlst_AndFlags()
    {
        var ocr = new FakeOcr { ListPresent = true, Set = "THB", Num = "172" };
        var game = new FakeGame { HasPlst = true };
        var svc = Build(ocr, game);

        var dto = await svc.MatchAsync(PortraitPng(), CardGame.Mtg, isFoil: false);

        Assert.True(dto.IsListReprint);
        Assert.False(dto.ListReprintUnresolved);
        Assert.Equal("plst", dto.SetCode);
        Assert.Equal("plst-id", dto.GameCardId);
        // The plst lookup must have been asked for with the "{SET}-{num}" key, filter bypassed (null).
        Assert.Contains(game.Seen, o => o?.SetCode == "plst" && o.CollectorNumber == "THB-172");
    }

    [Fact]
    public async Task GlyphDetected_LeadingZeroCollector_StripsZeroInPlstKey()
    {
        var ocr = new FakeOcr { ListPresent = true, Set = "ZNR", Num = "015" };
        var game = new FakeGame { HasPlst = true, PlstKey = "ZNR-15" };
        var svc = Build(ocr, game);

        var dto = await svc.MatchAsync(PortraitPng(), CardGame.Mtg, isFoil: false);

        Assert.Equal("plst", dto.SetCode);
        Assert.Contains(game.Seen, o => o?.SetCode == "plst" && o.CollectorNumber == "ZNR-15");
    }

    [Fact]
    public async Task GlyphDetected_OcrFailed_StillMatchesWithinPlst()
    {
        // The collector line didn't OCR (common on List frames), but the glyph did — identification
        // must still resolve via pHash/art matching hard-constrained to plst.
        var ocr = new FakeOcr { ListPresent = true, Set = null, Num = null };
        var game = new FakeGame { HasPlst = true };
        var svc = Build(ocr, game);

        var dto = await svc.MatchAsync(PortraitPng(), CardGame.Mtg, isFoil: false);

        Assert.True(dto.IsListReprint);
        Assert.False(dto.ListReprintUnresolved);
        Assert.Equal("plst", dto.SetCode); // resolved via pHash-in-plst despite no collector OCR
    }

    [Fact]
    public async Task GlyphDetected_PlstMissing_FallsBackToOriginal_AndFlagsUnresolved()
    {
        var ocr = new FakeOcr { ListPresent = true, Set = "THB", Num = "172" };
        var game = new FakeGame { HasPlst = false };
        var svc = Build(ocr, game);

        var dto = await svc.MatchAsync(PortraitPng(), CardGame.Mtg, isFoil: false);

        Assert.True(dto.IsListReprint);
        Assert.True(dto.ListReprintUnresolved);
        Assert.Equal("THB", dto.SetCode); // fell back to the original printing
    }

    [Fact]
    public async Task NoGlyph_NoRemap_NoFlags()
    {
        var ocr = new FakeOcr { ListPresent = false, Set = "THB", Num = "172" };
        var game = new FakeGame { HasPlst = true };
        var svc = Build(ocr, game);

        var dto = await svc.MatchAsync(PortraitPng(), CardGame.Mtg, isFoil: false);

        Assert.False(dto.IsListReprint);
        Assert.False(dto.ListReprintUnresolved);
        Assert.Equal("THB", dto.SetCode);
        Assert.DoesNotContain(game.Seen, o => o?.SetCode == "plst");
    }

    // --- fakes ---

    private sealed class FakeOcr : IOcrMatchingService
    {
        public bool ListPresent;
        public string? Set;
        public string? Num;
        public Dictionary<string, ulong> SymbolHashes { get; set; } = [];
        public Task<OcrMatchResult> AnalyzeCardAsync(byte[] d) => Task.FromResult(new OcrMatchResult());
        public (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] d) => ([], 0);
        public Task<(string? CollectorNumber, double Confidence)> DetectOptcgCollectorNumberAsync(byte[] d) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? CollectorNumber, double Confidence)> DetectRiftboundCollectorNumberAsync(byte[] d) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? CollectorNumber, double Confidence)> DetectCollectorNumberAsync(byte[] d, OcrCollectorSpec s) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? SetCode, string? CollectorNumber, double Confidence)> DetectMtgSetAndNumberAsync(byte[] d) => Task.FromResult((Set, Num, 0.9));
        public Task<(bool Present, double Confidence)> DetectMtgListSymbolAsync(byte[] d) => Task.FromResult((ListPresent, 0.9));
    }

    private sealed class FakeGame : ICardGameService
    {
        public bool HasPlst = true;
        public string PlstKey = "THB-172";
        public readonly List<OcrMatchResult?> Seen = [];

        public CardGame Game => CardGame.Mtg;
        public MatchDiagnostics? LastMatchDiagnostics => null;

        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null,
            IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null)
        {
            Seen.Add(ocrResult);
            var plstScoped = setFilter is not null && setFilter.Contains("plst");
            var plstCard = new CardMatch { Name = "Card", SetCode = "plst", CollectorNumber = PlstKey, GameSpecificId = "plst-id" };

            if (ocrResult?.SetCode is null)
                // No OCR: the initial pHash pass (no filter) returns null; the plst-constrained pHash pass
                // (filter = {plst}) resolves to the plst card when it's in the catalog.
                return plstScoped && HasPlst ? plstCard : null;

            if (string.Equals(ocrResult.SetCode, "plst", StringComparison.OrdinalIgnoreCase))
                // plst-constrained lookup: exact key hit, else pHash-in-plst.
                return HasPlst && (ocrResult.CollectorNumber == PlstKey || plstScoped) ? plstCard : null;

            // Original-set lookup.
            return new CardMatch { Name = "Card", SetCode = ocrResult.SetCode, CollectorNumber = ocrResult.CollectorNumber ?? "", GameSpecificId = "orig-id" };
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
