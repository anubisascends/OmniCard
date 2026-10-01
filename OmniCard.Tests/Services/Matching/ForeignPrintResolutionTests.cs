using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.CardMatching;
using OmniCard.CardMatching.Games;
using OmniCard.Data;
using OmniCard.Data.Catalogs;
using OmniCard.Imaging;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Matching;

namespace OmniCard.Tests.Services.Matching;

/// <summary>
/// Identification paths that don't rely on an English read of the card: repairing a misread set code
/// (<see cref="ScryfallService.CorrectOcrSetCodes"/> — Japanese SOI prints read "SO1"/"SO") and resolving
/// a 2008–2014 frame from its "© year … nnn/ttt" line alone (<see cref="ScryfallService.ResolveByCollectorLine"/>).
/// </summary>
public class ForeignPrintResolutionTests : IDisposable
{
    private const ulong EmissaryHash = 0x0F0F_0F0F_0F0F_0F0F;
    private const ulong LawkeeperHash = 0x3C3C_3C3C_3C3C_3C3C;
    private const ulong FarHash = 0xF0F0_F0F0_F0F0_F0F0; // 64 bits from EmissaryHash
    private const ulong OtherHash = 0xAAAA_AAAA_AAAA_AAAA;

    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<ScryfallDbContext> _factory;

    public ForeignPrintResolutionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ScryfallDbContext>().UseSqlite(_connection).Options;
        _factory = new TestScryfallDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        var emissaryArt = Guid.NewGuid();
        var lawkeeperArt = Guid.NewGuid();
        ctx.Cards.AddRange(
            // Modern frame: SOI #17 (English + Japanese), and the same collector number in neighbouring sets.
            Card("Emissary of the Sleepless", "soi", "17", "en", "2016-04-08", EmissaryHash, emissaryArt),
            Card("Emissary of the Sleepless", "soi", "17", "ja", "2016-04-08", EmissaryHash ^ 0x3, emissaryArt),
            Card("Sok Seventeen", "sok", "17", "en", "2005-06-03", FarHash),
            Card("Som Seventeen", "som", "17", "en", "2010-10-01", OtherHash),
            // 2003 frame: M12 #18 of 249 (English + Japanese), M11 #18 of 249, and a small set's #18.
            Card("Gideon's Lawkeeper", "m12", "18", "en", "2011-07-15", LawkeeperHash, lawkeeperArt),
            Card("Gideon's Lawkeeper", "m12", "18", "ja", "2011-07-15", LawkeeperHash ^ 0x1, lawkeeperArt),
            Card("M12 Last", "m12", "249", "en", "2011-07-15", OtherHash),
            Card("Infantry Veteran", "m11", "18", "en", "2010-07-16", FarHash),
            Card("M11 Last", "m11", "249", "en", "2010-07-16", OtherHash),
            Card("Gideon's Lawkeeper", "ddx", "18", "en", "2011-03-01", LawkeeperHash, lawkeeperArt));
        ctx.SaveChanges();
    }

    private static Card Card(string name, string set, string num, string lang, string released, ulong hash, Guid? art = null) => new()
    {
        Id = Guid.NewGuid(), OracleId = Guid.NewGuid(), Name = name, Lang = lang, Layout = "normal", TypeLine = "Creature",
        SetCode = set, SetName = set.ToUpperInvariant(), SetType = "expansion", CollectorNumber = num, Rarity = "common",
        BorderColor = "black", Frame = "2015", ReleasedAt = released, ImageHash = hash, IllustrationId = art ?? Guid.NewGuid(),
    };

    // ── CorrectOcrSetCodes ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SO1")] // the serif "I" read as a digit
    [InlineData("SO")]  // the "I" swallowed by the bullet
    public void CorrectOcrSetCodes_RepairsMisreadSet_ByImage(string misread)
    {
        var reads = CreateService().CorrectOcrSetCodes([new MtgPrintedIdentity(misread, "17", 2, "ja")], EmissaryHash ^ 0xF);

        var read = Assert.Single(reads);
        Assert.Equal("SOI", read.SetCode);
        Assert.Equal("17", read.CollectorNumber);
        Assert.Equal("ja", read.Language);
    }

    [Fact]
    public void CorrectOcrSetCodes_MergesReadsThatNowNameTheSamePrinting()
    {
        var reads = CreateService().CorrectOcrSetCodes(
            [new MtgPrintedIdentity("SO1", "17", 1), new MtgPrintedIdentity("SOI", "17", 1), new MtgPrintedIdentity("SO", "17", 1)],
            EmissaryHash);

        var read = Assert.Single(reads);
        Assert.Equal("SOI", read.SetCode);
        Assert.Equal(3, read.Votes);
    }

    [Fact]
    public void CorrectOcrSetCodes_LeavesKnownSetsAlone()
    {
        // "SOK" is a real set — never second-guessed, even when the image looks like SOI #17.
        var reads = CreateService().CorrectOcrSetCodes([new MtgPrintedIdentity("SOK", "17", 3)], EmissaryHash);

        Assert.Equal("SOK", Assert.Single(reads).SetCode);
    }

    [Theory]
    [InlineData("SO1", "SOI")] // a confusable-glyph swap is trusted at a looser image distance…
    [InlineData("SO", "SO")]   // …than a guessed missing character, at the same 14 bits
    public void CorrectOcrSetCodes_LengthEditsNeedACloserImage(string misread, string expected)
    {
        var reads = CreateService().CorrectOcrSetCodes([new MtgPrintedIdentity(misread, "17", 3)], EmissaryHash ^ 0xFFFF);

        Assert.Equal(expected, Assert.Single(reads).SetCode);
    }

    [Fact]
    public void CorrectOcrSetCodes_NoClearImageWinner_LeavesReadUnchanged()
    {
        // Nothing among the neighbour sets' #17s looks like the scan, so there's no evidence for a repair.
        var reads = CreateService().CorrectOcrSetCodes([new MtgPrintedIdentity("SO", "17", 3)], 0x0000_FFFF_0000_FFFF);

        Assert.Equal("SO", Assert.Single(reads).SetCode);
    }

    [Theory]
    [InlineData("SO1", "SOI", true)]
    [InlineData("S0I", "SOI", true)]
    [InlineData("SO", "SOI", true)]
    [InlineData("SOII", "SOI", true)]
    [InlineData("SO1", "SOM", false)] // 1 and M aren't confusable
    [InlineData("SOI", "SOI", false)]
    [InlineData("SO", "SOMX", false)]
    public void IsOcrNeighbour(string read, string code, bool expected)
    {
        Assert.Equal(expected, ScryfallService.IsOcrNeighbour(read, code));
    }

    // ── ResolveByCollectorLine ────────────────────────────────────────────────────────────────────────

    private static MtgPrintEvidence BottomLine(params string[] reads) => new() { BottomLineReads = reads };

    [Fact]
    public void ResolveByCollectorLine_IdentifiesPrintingFromNumberTotalAndYear()
    {
        // A Chinese M12 card: the title can't be read, but the copyright line is printed in English.
        var ev = BottomLine(
            "-- Steve rrescott j Bie T & c 1993-2011 Wizards of the Coast LLC 18/249",
            ". : WER c 1993-204 Wizards of the Coast LLC 18/249 . -_");

        var match = CreateService().ResolveByCollectorLine(LawkeeperHash ^ 0x1, null, ev, null);

        Assert.NotNull(match);
        Assert.Equal("Gideon's Lawkeeper", match.Name);
        Assert.Equal("m12", match.SetCode);   // ddx shares the art but has no #249
        Assert.Equal("18", match.CollectorNumber);
        Assert.Equal("en", match.Language);   // no printed language marker → the English row
    }

    [Fact]
    public void ResolveByCollectorLine_YearRulesOutPrintingsFromOtherYears()
    {
        // ©2008: M12 (2011) and M11 (2010) are both more than a year off, so the look-alike M12 #18 can't win.
        var ev = BottomLine("T & c 1993-2008 Wizards of the Coast LLC 18/249");

        Assert.Null(CreateService().ResolveByCollectorLine(LawkeeperHash, null, ev, null));
    }

    [Fact]
    public void ResolveByCollectorLine_NoFraction_ReturnsNull()
    {
        var ev = BottomLine("Steve Prescott T & c 1993-2011 Wizards of the Coast LLC");

        Assert.Null(CreateService().ResolveByCollectorLine(LawkeeperHash, null, ev, null));
    }

    private ScryfallService CreateService() => new(
        new StubHttpClientFactory(),
        _factory,
        new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
        new SetSymbolCache(new StubHttpClientFactory(), new DataPathService(Path.GetTempPath()), NullLogger<SetSymbolCache>.Instance),
        Options.Create(new ScryfallSettings { Languages = ["en"] }),
        NullLogger<ScryfallService>.Instance,
        new DataPathService(Path.GetTempPath()));

    public void Dispose() => _connection.Dispose();

    private class TestScryfallDbFactory(DbContextOptions<ScryfallDbContext> options) : IDbContextFactory<ScryfallDbContext>
    {
        public ScryfallDbContext CreateDbContext() => new(options);
    }

    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
