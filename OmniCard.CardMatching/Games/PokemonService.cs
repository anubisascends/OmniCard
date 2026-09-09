using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Settings;
using OmniCard.CardMatching.Search;
using OmniCard.Data.Catalogs;

namespace OmniCard.CardMatching.Games;

public sealed class PokemonService : TcgCsvGameService<PokemonDbContext>
{
    public PokemonService(IHttpClientFactory httpClientFactory, IDbContextFactory<PokemonDbContext> dbContextFactory,
        IPerceptualHashService hashService, IDataPathService dataPathService, ILogger<PokemonService> logger)
        : base(httpClientFactory, dbContextFactory, hashService, dataPathService, logger) { }

    protected override int CategoryId => 3;
    public override CardGame Game => CardGame.Pokemon;
    protected override string GameKey => "pokemon";

    // Searchable Pokémon attributes (from ExtendedDataJson). SourceKeys are TCGCSV extendedData names
    // (best-effort; tune against a live catalog if a field returns no matches).
    protected override IEnumerable<SearchFieldDefinition> GameSearchFields =>
    [
        new() { Canonical = "hp", Aliases = ["hp"], SourceKey = "HP",
                SupportedOps = [ComparisonOp.Contains, ComparisonOp.Exact, ComparisonOp.NotEqual,
                    ComparisonOp.LessThan, ComparisonOp.GreaterThan, ComparisonOp.LessOrEqual, ComparisonOp.GreaterOrEqual],
                Description = "Hit points (supports <, >, <=, >=).", Example = "hp>=200" },
        new() { Canonical = "stage", Aliases = ["stage"], SourceKey = "Stage", Description = "Evolution stage.", Example = "stage:basic" },
    ];

    protected override (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> rows) => MapSubtypePricesForTest(rows);

    // Pokémon prices: Normal + (Holofoil preferred over Reverse Holofoil) as the single foil slot.
    internal static (decimal? Normal, decimal? Foil) MapSubtypePricesForTest(List<TcgCsvPrice> rows)
    {
        decimal? P(string name) => rows.FirstOrDefault(r =>
            string.Equals(r.SubTypeName, name, StringComparison.OrdinalIgnoreCase))?.MarketPrice;
        return (P("Normal"), P("Holofoil") ?? P("Reverse Holofoil"));
    }

    // Pokémon collector numbers look like "123/198"; number sits bottom-left.
    public static readonly OcrCollectorSpec OcrSpec = new()
    {
        PortraitRegion = (0.03, 0.90, 0.35, 0.07),
        LandscapeRegion = (0.03, 0.88, 0.30, 0.09),
        Whitelist = "0123456789/",
        RegexPattern = @"(\d+\s*/\s*\d+)"
    };
}
