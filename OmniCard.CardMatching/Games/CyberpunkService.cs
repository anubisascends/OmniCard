using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Settings;
using OmniCard.CardMatching.Search;
using OmniCard.Data.Catalogs;

namespace OmniCard.CardMatching.Games;

/// <summary>Cyberpunk TCG (TCGCSV category 92). Catalog + prices come straight from TCGCSV like the
/// other <see cref="TcgCsvGameService{TContext}"/> games. Numbers are bare ("001", Beta "B001") and only
/// unique within a set, so a scan's OCR read is the printed set code plus the number (see
/// <c>OcrMatchingService.ReadCyberpunkCodeAsync</c>): the number finds the rows, the set code
/// (<see cref="PrintedSetCode"/>) narrows them, and the image hash settles the rest.</summary>
public sealed class CyberpunkService : TcgCsvGameService<CyberpunkDbContext>
{
    public CyberpunkService(IHttpClientFactory httpClientFactory, IDbContextFactory<CyberpunkDbContext> dbContextFactory,
        IPerceptualHashService hashService, IDataPathService dataPathService, ILogger<CyberpunkService> logger)
        : base(httpClientFactory, dbContextFactory, hashService, dataPathService, logger) { }

    protected override int CategoryId => 92;
    public override CardGame Game => CardGame.Cyberpunk;
    protected override string GameKey => "cyberpunk";

    // "Pre-Release Retail" and "Pre-Release Beta" are both PRR with overlapping numbers.
    protected override bool DisambiguateSharedSetCodes => true;

    private static readonly ComparisonOp[] NumericOps =
    [
        ComparisonOp.Contains, ComparisonOp.Exact, ComparisonOp.NotEqual,
        ComparisonOp.LessThan, ComparisonOp.GreaterThan, ComparisonOp.LessOrEqual, ComparisonOp.GreaterOrEqual,
    ];

    // Searchable Cyberpunk attributes (ExtendedDataJson, queried in memory). SourceKey is the TCGCSV
    // extendedData name. Color replaces the core MTG-letter color field: c:r / color:red.
    protected override IEnumerable<SearchFieldDefinition> GameSearchFields =>
    [
        new()
        {
            Canonical = "color", Aliases = ["color", "c"], SourceKey = "Color",
            ValueAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["r"] = "Red", ["g"] = "Green", ["b"] = "Blue", ["y"] = "Yellow",
            },
            Description = "Color (Red, Green, Blue, Yellow). Shorthands: r, g, b, y.",
            Example = "c:red",
        },
        new() { Canonical = "cost", Aliases = ["cost"], SourceKey = "Cost", SupportedOps = NumericOps,
                Description = "Cost (supports <, >, <=, >=).", Example = "cost<=3" },
        new() { Canonical = "power", Aliases = ["power", "pow"], SourceKey = "Power", SupportedOps = NumericOps,
                Description = "Power.", Example = "power>=6" },
        new() { Canonical = "ram", Aliases = ["ram"], SourceKey = "RAM", SupportedOps = NumericOps,
                Description = "RAM (supports <, >, <=, >=).", Example = "ram<=2" },
        new() { Canonical = "trait", Aliases = ["trait", "traits"], SourceKey = "Tags",
                Description = "Trait (Arasaka, Merc, Samurai, …).", Example = "trait:arasaka" },
        new() { Canonical = "eddies", Aliases = ["eddies", "eddie"], SourceKey = "Eddies",
                Description = "Has the Eddies icon (true / false).", Example = "eddies:true" },
        new() { Canonical = "text", Aliases = ["text", "o", "oracle"], SourceKey = "Description",
                Description = "Rules text.", Example = "o:blocker" },
    ];

    /// <summary>The code a set prints in its product line ("MS01 - WNC [A]" → "WNC"), taken from TCGplayer's
    /// set name: the initials of its capitalised words, before any " - Retail"/" - Beta" suffix ("Welcome to
    /// Night City - Beta" → "WNC"). Retail and Beta share it; their numbers differ ("120" vs "B120").</summary>
    internal static string PrintedSetCode(string setName)
    {
        var name = setName.Split(" - ")[0];
        return new string(name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsUpper(w[0]) || char.IsDigit(w[0]))
            .Select(w => char.ToUpperInvariant(w[0]))
            .ToArray());
    }

    /// <summary>Bare numbers repeat across sets ("006" is in the main set, the starter decks and the promos),
    /// so keep the rows whose set prints the OCR'd set code — or the nearest one, a letter off ("WAC" →
    /// "WNC"). A set whose printed code isn't its name's initials keeps every row (the image decides). Rows
    /// whose art clearly isn't the scan's are dropped when some row's art is, so a misread set code can't
    /// pin an unrelated card.</summary>
    protected override List<TcgCsvCard> NarrowOcrCandidates(List<TcgCsvCard> candidates, OcrMatchResult ocr,
        ulong imageHash, int maxDistance)
    {
        var pool = candidates;
        if (!string.IsNullOrWhiteSpace(ocr.SetCode))
        {
            var read = ocr.SetCode.ToUpperInvariant();
            var scored = candidates
                .Select(c => (Card: c, Distance: CollectorNumberFuzzyMatcher.RawDistance(PrintedSetCode(c.SetName), read)))
                .ToList();
            var best = scored.Min(t => t.Distance);
            if (best <= 1) pool = scored.Where(t => t.Distance == best).Select(t => t.Card).ToList();
        }

        bool LooksLikeScan(TcgCsvCard c) =>
            c.ImageHash is ulong h && OmniCard.Imaging.PerceptualHashService.HammingDistance(imageHash, h) <= maxDistance;
        if (pool.Any(LooksLikeScan)) return pool.Where(LooksLikeScan).ToList();
        if (pool.Count < candidates.Count && candidates.Any(LooksLikeScan)) return candidates.Where(LooksLikeScan).ToList();
        return pool;
    }

    protected override (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> rows) => MapSubtypePricesForTest(rows);

    internal static (decimal? Normal, decimal? Foil) MapSubtypePricesForTest(List<TcgCsvPrice> rows)
    {
        decimal? P(string name) => rows.FirstOrDefault(r =>
            string.Equals(r.SubTypeName, name, StringComparison.OrdinalIgnoreCase))?.MarketPrice;
        return (P("Normal"), P("Foil"));
    }

    protected override void MapExtendedData(TcgCsvProduct product, TcgCsvCard card)
    {
        base.MapExtendedData(product, card);
        // TCGCSV serves the same attributes in several spellings ("x2" RAM, "04" cost, "TRUE"/"true").
        // Normalize them so ram<=2 / cost=4 / eddies:true compare as expected, and store the cleaned
        // blob (it is only read for search and the card-detail attribute list).
        var changed = false;
        foreach (var e in product.ExtendedData)
        {
            var normalized = NormalizeAttribute(e.Name, e.Value);
            if (normalized == e.Value) continue;
            e.Value = normalized;
            changed = true;
        }
        if (changed) card.ExtendedDataJson = JsonSerializer.Serialize(product.ExtendedData, TcgCsvJsonOptions);
    }

    internal static string NormalizeAttribute(string name, string value)
    {
        var v = value.Trim();
        switch (name.ToUpperInvariant())
        {
            case "RAM":
                if (v.StartsWith('x') || v.StartsWith('X')) v = v[1..];
                return StripLeadingZeros(v);
            case "COST":
            case "POWER":
                return StripLeadingZeros(v);
            case "EDDIES":
                return v.Equals("true", StringComparison.OrdinalIgnoreCase) ? "True"
                    : v.Equals("false", StringComparison.OrdinalIgnoreCase) ? "False" : v;
            default:
                return value;
        }
    }

    // "04" → "4", "05" → "5", "00" → "0"; leaves "6+" and non-numbers alone.
    private static string StripLeadingZeros(string v)
    {
        var i = 0;
        while (i < v.Length - 1 && v[i] == '0' && char.IsDigit(v[i + 1])) i++;
        return v[i..];
    }
}
