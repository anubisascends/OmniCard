using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Collection.Inventory;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Settings;
using OmniCard.Shared.Storage;
using OmniCard.Tests.Fakes;
using OmniCard.Web.Api.Controllers;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>Covers <c>POST /api/import/url</c>: a fetched Moxfield/Archidekt deck lands in the target
/// location as owned lots — exact printing, finish kept, repeat printings merged, name-only fallback
/// reported, unknown cards reported, duplicates optionally skipped.</summary>
public class ImportControllerUrlTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly ImportController _controller;
    private readonly StubDecklists _decklists = new();
    private readonly int _boxId;

    private static readonly CardMatch Bolt2x2 = Printing("bolt-2x2", "Lightning Bolt", "2X2", "117");
    private static readonly CardMatch BoltM10 = Printing("bolt-m10", "Lightning Bolt", "M10", "146");
    private static readonly CardMatch SolRing = Printing("sol-scd", "Sol Ring", "SCD", "276");

    public ImportControllerUrlTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();
        var factory = new Factory(_opts);

        var all = new[] { Bolt2x2, BoltM10, SolRing };
        var gs = new ConfigurableGameService
        {
            OnSearchCards = (q, _) => all.Where(p => q.Contains($"set:{p.SetCode}") && q.Contains($"cn:{p.CollectorNumber}")).ToList(),
            OnGetPrintings = name => all.Where(p => p.Name == name).ToList(),
            // M10 Bolt is the cheapest printing.
            OnGetCurrentPrices = (ids, _) => ids.ToDictionary(id => id, id => id == "bolt-m10" ? 1m : 3m),
        };

        _controller = new ImportController(null!, new WebBinderCardService(factory, new StubDataPath()), _decklists,
            new RecordingCardService(gs), null!);
        _boxId = new StorageContainerService(factory).Create("Deck Box", ContainerType.Box).Id;
    }

    public void Dispose() => _conn.Dispose();

    private static CardMatch Printing(string id, string name, string set, string cn) => new()
    {
        GameSpecificId = id, Name = name, SetCode = set, CollectorNumber = cn, SetName = set + " set", Rarity = "common",
    };

    private static T Value<T>(ActionResult<T> r) => r.Result is ObjectResult o ? (T)o.Value! : r.Value!;

    private ImportUrlRequest Request(bool skipDuplicates = false) => new()
    {
        Url = "https://moxfield.com/decks/abc", Game = "Mtg", ContainerId = _boxId, Condition = "LP",
        SkipDuplicates = skipDuplicates,
    };

    private List<(string GameCardId, int Qty, bool Foil, string? FoilType, string Condition, int? LocationId)> Lots()
    {
        using var ctx = new OmniCardDbContext(_opts);
        return ctx.Lots.Include(l => l.Product).AsEnumerable()
            .Select(l => (l.Product.GameCardId!, l.Quantity, l.Product.Foil, l.Product.FoilType, l.Condition, l.LocationId))
            .ToList();
    }

    [Fact]
    public async Task Url_ImportsExactPrintingsWithFinish_IntoLocation()
    {
        _decklists.FetchResult = ("My Deck",
        [
            new DecklistEntry(4, "Lightning Bolt", "2X2", "117"),
            new DecklistEntry(1, "Sol Ring", "SCD", "276", "Etched"),
        ]);

        var result = Value(await _controller.Url(Request()));

        Assert.Equal("My Deck", result.DeckName);
        Assert.Equal(5, result.Imported);
        Assert.Equal(5, result.TotalCards);
        Assert.Empty(result.UnresolvedNames);
        Assert.Empty(result.SubstitutedNames);

        var lots = Lots();
        Assert.Equal(2, lots.Count);
        var bolt = Assert.Single(lots, l => l.GameCardId == "bolt-2x2");
        Assert.Equal((4, false, (string?)null, "LP", (int?)_boxId), (bolt.Qty, bolt.Foil, bolt.FoilType, bolt.Condition, bolt.LocationId));
        var ring = Assert.Single(lots, l => l.GameCardId == "sol-scd");
        Assert.True(ring.Foil);
        Assert.Equal("Etched", ring.FoilType);
    }

    [Fact]
    public async Task Url_MergesRepeatPrintings_AndReportsSubstitutedAndUnresolved()
    {
        _decklists.FetchResult = ("Deck",
        [
            new DecklistEntry(1, "Lightning Bolt", "M10", "146"),
            new DecklistEntry(2, "Lightning Bolt", "ZZZ", "999"),  // printing not in catalog → cheapest (M10)
            new DecklistEntry(1, "Nonexistent Card", "ABC", "1"),
        ]);

        var result = Value(await _controller.Url(Request()));

        Assert.Equal(3, result.Imported);
        Assert.Equal(4, result.TotalCards);
        Assert.Equal(["Lightning Bolt"], result.SubstitutedNames);
        Assert.Equal(["Nonexistent Card"], result.UnresolvedNames);
        var lot = Assert.Single(Lots());
        Assert.Equal(("bolt-m10", 3), (lot.GameCardId, lot.Qty));
    }

    [Fact]
    public async Task Url_SkipDuplicates_SkipsPrintingsAlreadyOwnedInSameCondition()
    {
        _decklists.FetchResult = ("Deck", [new DecklistEntry(2, "Sol Ring", "SCD", "276")]);
        await _controller.Url(Request());

        _decklists.FetchResult = ("Deck",
        [
            new DecklistEntry(2, "Sol Ring", "SCD", "276"),
            new DecklistEntry(1, "Lightning Bolt", "2X2", "117"),
        ]);
        var result = Value(await _controller.Url(Request(skipDuplicates: true)));

        Assert.Equal(1, result.Imported);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(2, Lots().Count);
    }

    [Fact]
    public async Task Url_RequiresLocation_AndFetchableUrl()
    {
        Assert.IsType<BadRequestObjectResult>((await _controller.Url(Request() with { ContainerId = 0 })).Result);

        _decklists.FetchResult = null;
        Assert.IsType<BadRequestObjectResult>((await _controller.Url(Request())).Result);
    }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    private sealed class StubDecklists : IDecklistService
    {
        public (string DeckName, List<DecklistEntry> Entries)? FetchResult { get; set; }
        public Task<(string DeckName, List<DecklistEntry> Entries)?> FetchDecklistAsync(string url) => Task.FromResult(FetchResult);
        public (string DeckName, List<DecklistEntry> Entries) ParseDecklistText(string text) => throw new NotImplementedException();
        public List<DecklistEntry> ParseDecklistPrintings(string text) => throw new NotImplementedException();
        public DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries, CardGame game, IReadOnlyCollection<int>? siteIds = null) => throw new NotImplementedException();
    }

    private sealed class StubDataPath : IDataPathService
    {
        public string DataDirectory => Path.GetTempPath();
        public string ScansDirectory => Path.GetTempPath();
        public string TempScansDirectory => Path.GetTempPath();
        public string SymbolsCacheDirectory => Path.GetTempPath();
        public string LogsDirectory => Path.GetTempPath();
        public string TradesDirectory => Path.GetTempPath();
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
