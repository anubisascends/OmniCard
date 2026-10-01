using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Settings;
using OmniCard.CardMatching.Search;
using OmniCard.Data.Catalogs;

namespace OmniCard.CardMatching.Games;

public sealed class YugiohService : TcgCsvGameService<YugiohDbContext>
{
    public YugiohService(IHttpClientFactory httpClientFactory, IDbContextFactory<YugiohDbContext> dbContextFactory,
        IPerceptualHashService hashService, IDataPathService dataPathService, ILogger<YugiohService> logger)
        : base(httpClientFactory, dbContextFactory, hashService, dataPathService, logger) { }

    protected override int CategoryId => 2;
    public override CardGame Game => CardGame.YuGiOh;
    protected override string GameKey => "yugioh";

    // Searchable Yu-Gi-Oh! attributes (from ExtendedDataJson). SourceKeys are TCGCSV extendedData names
    // (best-effort; tune against a live catalog if a field returns no matches).
    protected override IEnumerable<SearchFieldDefinition> GameSearchFields =>
    [
        new() { Canonical = "attribute", Aliases = ["attribute", "attr"], SourceKey = "Attribute",
                Description = "Monster attribute (DARK, LIGHT, …).", Example = "attribute:dark" },
        new() { Canonical = "level", Aliases = ["level", "lvl", "rank"], SourceKey = "Level",
                SupportedOps = [ComparisonOp.Contains, ComparisonOp.Exact, ComparisonOp.NotEqual,
                    ComparisonOp.LessThan, ComparisonOp.GreaterThan, ComparisonOp.LessOrEqual, ComparisonOp.GreaterOrEqual],
                Description = "Level / Rank (supports <, >, <=, >=).", Example = "level>=8" },
        new() { Canonical = "atk", Aliases = ["atk"], SourceKey = "Attack",
                SupportedOps = [ComparisonOp.Contains, ComparisonOp.Exact, ComparisonOp.NotEqual,
                    ComparisonOp.LessThan, ComparisonOp.GreaterThan, ComparisonOp.LessOrEqual, ComparisonOp.GreaterOrEqual],
                Description = "ATK (supports <, >, <=, >=).", Example = "atk>=3000" },
        new() { Canonical = "def", Aliases = ["def"], SourceKey = "Defense",
                SupportedOps = [ComparisonOp.Contains, ComparisonOp.Exact, ComparisonOp.NotEqual,
                    ComparisonOp.LessThan, ComparisonOp.GreaterThan, ComparisonOp.LessOrEqual, ComparisonOp.GreaterOrEqual],
                Description = "DEF (supports <, >, <=, >=).", Example = "def>=2500" },
    ];

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
            // The code prints just below the artwork on the lower-right; its exact height drifts a
            // little card-to-card (Spell/Trap sit a touch higher than Monsters). ONE tall band frames
            // the whole line with padding so glyph tops aren't clipped — the earlier pair of 2.8%-tall
            // bands at y=0.723/0.751 cut the tops on many Monsters (measured ~0/294 usable on a real
            // batch). The wider X (0.60) keeps the leading character; PSM SparseText (below) is robust
            // to the small amount of art-frame/border noise a taller crop catches. Re-validated on the
            // 2026091401 batch of 294 real scans.
            (0.60, 0.706, 0.32, 0.044),
        ],
        LandscapeRegions =
        [
            (0.60, 0.706, 0.32, 0.044),
        ],
        Whitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-",
        RegexPattern = @"([A-Z0-9]+-[A-Z]{0,2}\d+)",
        Binarize = true,
        LooseExtraction = true,
        // SingleLine reads these short-wide strips as garbage ("DAMA-EN012" → "OLSIWANLEOY"); sparse
        // text mode reads them correctly. This is the single biggest lever on Yu-Gi-Oh! OCR accuracy.
        PageSegMode = OcrPageSegMode.SparseText,
        // Holofoil reads often turn the collector digits into letters (…EN012 → …ENULZ); let those
        // through — FuzzyOcrMatch canonicalizes letters back to digits against the catalog.
        AllowLetterOnlyToken = true,
    };

    // OCR of small holofoil set codes is noisy; resolve reads to the catalog fuzzily + by pHash.
    protected override bool UseFuzzyOcrMatch => true;

    // "{SET}-{REGION}{NUMBER}", e.g. "RA05-DE085", "LOB-G001", "PHNI-JP001". Group 2 is the region.
    private static readonly System.Text.RegularExpressions.Regex RegionCodePattern =
        new(@"^([A-Z0-9]+)-([A-Z]{1,2})(\d+)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // Printed region code → card language. Two-letter codes are the modern form; the single letters are
    // the early European prints (LOB-G001 German, LOB-E001 English, …). AE is English (Asia).
    private static readonly Dictionary<string, string> RegionLanguages = new()
    {
        ["EN"] = "en", ["E"] = "en", ["AE"] = "en",
        ["DE"] = "de", ["G"] = "de",
        ["FR"] = "fr", ["F"] = "fr",
        ["IT"] = "it", ["I"] = "it",
        ["SP"] = "es", ["S"] = "es",
        ["PT"] = "pt", ["P"] = "pt",
        ["JP"] = "ja", ["JA"] = "ja",
        ["KR"] = "ko",
        ["SC"] = "zhs",
        ["TC"] = "zht",
    };

    /// <summary>
    /// Maps an OCR'd Yu-Gi-Oh! set code's region onto the English catalog: the TCGCSV catalog only holds
    /// English printings, but the European TCG languages (and the modern OCG core sets) print the same set
    /// prefix + number with a different region — "RA05-DE085" is the German "RA05-EN085". The region is
    /// swapped to EN (single-letter early-EU regions to E) and its language returned via
    /// <paramref name="language"/>; the fuzzy/pHash catalog match takes it from there. Codes without a
    /// recognized region come back unchanged with a null language.
    /// </summary>
    public static string NormalizeRegionCode(string collectorNumber, out string? language)
    {
        language = null;
        var m = RegionCodePattern.Match(collectorNumber.Trim().ToUpperInvariant());
        if (!m.Success || !RegionLanguages.TryGetValue(m.Groups[2].Value, out var lang))
            return collectorNumber;

        language = lang;
        if (lang == CardLanguages.English)
            return collectorNumber;
        var englishRegion = m.Groups[2].Value.Length == 1 ? "E" : "EN";
        return $"{m.Groups[1].Value}-{englishRegion}{m.Groups[3].Value}";
    }
}
