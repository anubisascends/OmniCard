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

    // FFTCG reprints store several printings joined by '/' (e.g. "Re-103C/11-072R"); a scanned card
    // prints only one, so match an OCR'd code against any '/'-delimited part. See FindClosestMatch.
    protected override bool SplitReprintNumbers => true;

    protected override (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> rows) => MapSubtypePricesForTest(rows);

    internal static (decimal? Normal, decimal? Foil) MapSubtypePricesForTest(List<TcgCsvPrice> rows)
    {
        decimal? P(string name) => rows.FirstOrDefault(r =>
            string.Equals(r.SubTypeName, name, StringComparison.OrdinalIgnoreCase))?.MarketPrice;
        return (P("Normal"), P("Foil"));
    }

    // FFTCG prints the set code as "{opus}-{number}{rarity}" (e.g. "29-048C", "1-001H") on the
    // bottom credit line — horizontally centred, directly below the ©/illustrator lines, over a
    // light footer (occasionally over full-art). Rather than a fragile thin band (the code's exact
    // vertical position drifts card-to-card and single-line OCR breaks if it catches the credit line
    // above), we OCR the whole bottom-left credit block as a multi-line text block and let the regex
    // isolate the code — the surrounding credit text contains no "{1-2 digits}-{3 digits}{letter}"
    // token. The wide crop stops short of the card's right edge so it excludes right-side art (e.g.
    // the large power number on some full-art cards). Tuned against real opus-29 scans (4/5 read;
    // the miss falls through to pHash). See FftcgOcrHarness for the tuning sweep.
    public static readonly OcrCollectorSpec OcrSpec = new()
    {
        PortraitRegion = (0.02, 0.900, 0.64, 0.098),
        LandscapeRegion = (0.02, 0.880, 0.60, 0.110),
        Whitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-",
        // Exact FFTCG shape: 1-2 digit opus, 3-digit card number, single rarity letter. The strict
        // \d{3} (not \d+) rejects art-noise reads that inflate the digit count (e.g. "29-1100F").
        RegexPattern = @"(\d{1,2}-\d{3}[A-Z])",
        Binarize = true,
        MultiLine = true,
    };
}
