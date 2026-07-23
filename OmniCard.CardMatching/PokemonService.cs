using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.CardMatching;

public sealed class PokemonService : TcgCsvGameService<PokemonDbContext>
{
    public PokemonService(IHttpClientFactory httpClientFactory, IDbContextFactory<PokemonDbContext> dbContextFactory,
        IPerceptualHashService hashService, IDataPathService dataPathService, ILogger<PokemonService> logger)
        : base(httpClientFactory, dbContextFactory, hashService, dataPathService, logger) { }

    protected override int CategoryId => 3;
    public override CardGame Game => CardGame.Pokemon;
    protected override string GameKey => "pokemon";

    // Pokémon prices: Normal + (Holofoil preferred over Reverse Holofoil) as the single foil slot.
    protected override (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> rows)
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
