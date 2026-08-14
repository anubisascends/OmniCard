using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.CardMatching;

public sealed class YugiohService : TcgCsvGameService<YugiohDbContext>
{
    public YugiohService(IHttpClientFactory httpClientFactory, IDbContextFactory<YugiohDbContext> dbContextFactory,
        IPerceptualHashService hashService, IDataPathService dataPathService, ILogger<YugiohService> logger)
        : base(httpClientFactory, dbContextFactory, hashService, dataPathService, logger) { }

    protected override int CategoryId => 2;
    public override CardGame Game => CardGame.YuGiOh;
    protected override string GameKey => "yugioh";

    protected override (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> rows) => MapSubtypePricesForTest(rows);

    // Yu-Gi-Oh! sub-types are editions, not foils. Use Unlimited as the reference "normal" price
    // (fallback to Limited, then 1st Edition, then any). No distinct foil price.
    internal static (decimal? Normal, decimal? Foil) MapSubtypePricesForTest(List<TcgCsvPrice> rows)
    {
        decimal? P(string name) => rows.FirstOrDefault(r =>
            string.Equals(r.SubTypeName, name, StringComparison.OrdinalIgnoreCase))?.MarketPrice;
        var normal = P("Unlimited") ?? P("Limited") ?? P("1st Edition") ?? rows.FirstOrDefault()?.MarketPrice;
        return (normal, null);
    }

    // Yu-Gi-Oh! set codes (e.g. "GRCR-EN060") print in the lower-right: just below the artwork on
    // Spell/Trap cards, lower down (above the ATK/DEF band) on Monsters. We try both bands. The text
    // is small and low-contrast (worst on holofoil Collector's Rares), so the crop is binarized and
    // the read is matched to the catalog fuzzily rather than exactly — see FuzzyOcrMatch.
    public static readonly OcrCollectorSpec OcrSpec = new()
    {
        PortraitRegions =
        [
            // The code prints just below the artwork; its exact height drifts a little card-to-card,
            // so two thin overlapping bands cover the range without a tall crop swallowing the art
            // frame's edge (which wrecks single-line OCR). Validated on Spell/Trap and Collector's
            // Rare Monster scans. (A band down by the bottom edge is avoided: on Monsters it reads
            // the ATK/DEF line, which mimics a code — "DEF/ 800" → "…DEF800".)
            (0.70, 0.723, 0.28, 0.028),
            (0.70, 0.751, 0.28, 0.028),
        ],
        LandscapeRegions =
        [
            (0.70, 0.723, 0.28, 0.028),
            (0.70, 0.751, 0.28, 0.028),
        ],
        Whitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-",
        RegexPattern = @"([A-Z0-9]+-[A-Z]{0,2}\d+)",
        Binarize = true,
        LooseExtraction = true,
    };

    // OCR of small holofoil set codes is noisy; resolve reads to the catalog fuzzily + by pHash.
    protected override bool UseFuzzyOcrMatch => true;
}
