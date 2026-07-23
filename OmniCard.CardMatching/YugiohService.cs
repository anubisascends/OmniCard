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

    // Yu-Gi-Oh! set codes look like "LOB-EN001"; printed lower-left/right.
    public static readonly OcrCollectorSpec OcrSpec = new()
    {
        PortraitRegion = (0.55, 0.88, 0.42, 0.06),
        LandscapeRegion = (0.55, 0.86, 0.42, 0.08),
        Whitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-",
        RegexPattern = @"([A-Z0-9]+-[A-Z]{0,2}\d+)"
    };
}
