using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.CardMatching;

public sealed class FinalFantasyService : TcgCsvGameService<FinalFantasyDbContext>
{
    public FinalFantasyService(IHttpClientFactory httpClientFactory, IDbContextFactory<FinalFantasyDbContext> dbContextFactory,
        IPerceptualHashService hashService, IDataPathService dataPathService, ILogger<FinalFantasyService> logger)
        : base(httpClientFactory, dbContextFactory, hashService, dataPathService, logger) { }

    protected override int CategoryId => 24;
    public override CardGame Game => CardGame.FinalFantasy;
    protected override string GameKey => "fftcg";

    protected override (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> rows) => MapSubtypePricesForTest(rows);

    internal static (decimal? Normal, decimal? Foil) MapSubtypePricesForTest(List<TcgCsvPrice> rows)
    {
        decimal? P(string name) => rows.FirstOrDefault(r =>
            string.Equals(r.SubTypeName, name, StringComparison.OrdinalIgnoreCase))?.MarketPrice;
        return (P("Normal"), P("Foil"));
    }

    // FFTCG collector numbers look like "1-001H"; printed bottom-left.
    public static readonly OcrCollectorSpec OcrSpec = new()
    {
        PortraitRegion = (0.03, 0.92, 0.30, 0.06),
        LandscapeRegion = (0.03, 0.90, 0.28, 0.08),
        Whitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-",
        RegexPattern = @"(\d+-\d+[A-Z]?)"
    };
}
