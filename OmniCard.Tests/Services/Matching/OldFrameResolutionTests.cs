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
/// <see cref="ScryfallService.ResolveOldFramePrinting"/> against a seeded catalog: the title identifies the
/// card (over a wrong pHash guess), the printing comes from the physical evidence, and modern-frame cards
/// are left to the OCR-first pipeline.
/// </summary>
public class OldFrameResolutionTests : IDisposable
{
    private const ulong SageOwlHash = 0x0F0F_0F0F_0F0F_0F0F;
    private const ulong DeathcapHash = 0xF0F0_F0F0_F0F0_F0F0;
    private const ulong NafsAspHash = 0x1234_5678_9ABC_DEF0;
    private const ulong ModernHash = 0x5555_5555_5555_5555;

    private static readonly Guid SageOwlId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DeathcapId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid NafsArnId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Nafs4edId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<ScryfallDbContext> _factory;

    public OldFrameResolutionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ScryfallDbContext>().UseSqlite(_connection).Options;
        _factory = new TestScryfallDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.Cards.AddRange(
            Card(SageOwlId, "Sage Owl", "wth", "52", "black", "1997", "1997-06-09", SageOwlHash),
            Card(DeathcapId, "Deathcap Glade", "sos", "253", "black", "2015", "2026-01-01", DeathcapHash),
            Card(NafsArnId, "Nafs Asp", "arn", "52", "black", "1993", "1993-12-17", NafsAspHash),
            Card(Nafs4edId, "Nafs Asp", "4ed", "264", "white", "1993", "1995-04-01", NafsAspHash),
            Card(Guid.NewGuid(), "Modern Thing", "mkm", "12", "black", "2015", "2024-02-09", ModernHash));
        ctx.SaveChanges();
    }

    private static Card Card(Guid id, string name, string set, string num, string border, string frame, string released, ulong hash) => new()
    {
        Id = id, OracleId = Guid.NewGuid(), Name = name, Lang = "en", Layout = "normal", TypeLine = "Creature",
        SetCode = set, SetName = set.ToUpperInvariant(), SetType = "expansion", CollectorNumber = num, Rarity = "common",
        BorderColor = border, Frame = frame, ReleasedAt = released, ImageHash = hash,
    };

    private static CardMatch Current(Guid id, string name, string set) =>
        new() { Name = name, SetCode = set, GameSpecificId = id.ToString(), Source = new object() };

    [Fact]
    public void Title_OverridesWrongPHashGuess()
    {
        var svc = CreateService();
        // pHash guessed Deathcap Glade; the (one-letter-off) title and the image both say Sage Owl.
        var ev = new MtgPrintEvidence { TitleReads = ["Hage Owl"], BorderColor = "black" };
        var scanHash = SageOwlHash ^ 0xFF; // 8 bits from Sage Owl

        var res = svc.ResolveOldFramePrinting(scanHash, null, ev, null, null, Current(DeathcapId, "Deathcap Glade", "sos"));

        Assert.NotNull(res?.Match);
        Assert.Equal("Sage Owl", res.Match.Name);
        Assert.Equal("wth", res.Match.SetCode);
    }

    [Fact]
    public void Untitled_RepicksPrintingOfPHashMatch_FromBorder()
    {
        var svc = CreateService();
        var ev = new MtgPrintEvidence { BorderColor = "white" };

        var res = svc.ResolveOldFramePrinting(NafsAspHash, null, ev, null, null, Current(NafsArnId, "Nafs Asp", "arn"));

        Assert.Equal("4ed", res?.Match?.SetCode);
    }

    [Fact]
    public void SetFilter_BoundsThePrintings()
    {
        var svc = CreateService();
        var ev = new MtgPrintEvidence { TitleReads = ["Nafs Asp"], BorderColor = "white" };

        var res = svc.ResolveOldFramePrinting(NafsAspHash, null, ev, null, new HashSet<string> { "arn" }, null);

        Assert.Equal("arn", res?.Match?.SetCode);
    }

    [Fact]
    public void ModernFrameCard_IsLeftToTheModernPipeline()
    {
        var svc = CreateService();
        var ev = new MtgPrintEvidence { TitleReads = ["Modern Thing"], BorderColor = "black" };

        Assert.Null(svc.ResolveOldFramePrinting(ModernHash, null, ev, null, null, null));
    }

    [Fact]
    public void NothingIdentified_ReturnsNull()
    {
        var svc = CreateService();
        var ev = new MtgPrintEvidence { TitleReads = ["zzzz qqqq"] };

        Assert.Null(svc.ResolveOldFramePrinting(0, null, ev, null, null, null));
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
