using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Api.Contracts;
using OmniCard.Collection.ImportExport;
using OmniCard.Collection.Inventory;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Settings;
using OmniCard.Shared.Storage;
using OmniCard.Tests.Fakes;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>Covers the Location view's all-or-nothing import: every line lands in the route's location
/// (whatever the file says), duplicates are never skipped, and any bad line rejects the whole import
/// with a per-line explanation and nothing written.</summary>
public class LocationImportServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly LocationImportService _service;
    private readonly StubDecklists _decklists = new();
    private readonly Factory _factory;
    private readonly int _boxId;
    private readonly List<string> _tempFiles = [];

    private static readonly CardMatch Bolt2x2 = Printing("bolt-2x2", "Lightning Bolt", "2X2", "Double Masters 2022", "117");
    private static readonly CardMatch BoltM10 = Printing("bolt-m10", "Lightning Bolt", "M10", "Magic 2010", "146");
    private static readonly CardMatch SolRing = Printing("sol-scd", "Sol Ring", "SCD", "Starter Commander Decks", "276");

    public LocationImportServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();
        _factory = new Factory(_opts);

        var all = new[] { Bolt2x2, BoltM10, SolRing };
        var gs = new ConfigurableGameService
        {
            OnSearchCards = (q, _) => all.Where(p => q.Contains($"set:{p.SetCode}") && q.Contains($"cn:{p.CollectorNumber}")).ToList(),
            OnGetPrintings = name => all.Where(p => p.Name == name).ToList(),
            OnGetCurrentPrices = (ids, _) => ids.ToDictionary(id => id, id => id == "bolt-m10" ? 1m : 3m),
            OnFindCardById = id => all.Any(p => p.GameSpecificId == id) ? new object() : null,
        };

        _service = new LocationImportService(
            _factory,
            new CsvExportImportService(null, null, NullLogger<CsvExportImportService>.Instance),
            new WebBinderCardService(_factory, new StubDataPath()),
            _decklists,
            new RecordingCardService(gs),
            NullLogger<LocationImportService>.Instance);
        _boxId = NewLocation("Red Box", ContainerType.Box);
    }

    public void Dispose()
    {
        _conn.Dispose();
        foreach (var f in _tempFiles) File.Delete(f);
    }

    private static CardMatch Printing(string id, string name, string set, string setName, string cn) => new()
    {
        GameSpecificId = id, Name = name, SetCode = set, SetName = setName, CollectorNumber = cn, Rarity = "common",
    };

    private int NewLocation(string name, ContainerType type, CardGame? game = null)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var c = new StorageContainer { Name = name, ContainerType = type, Game = game };
        ctx.StorageContainers.Add(c);
        ctx.SaveChanges();
        return c.Id;
    }

    private string CsvFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"loc-import-test-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private LocationImportService.Outcome ImportCsv(string content, int? locationId = null) =>
        _service.ImportCsv(locationId ?? _boxId, CsvFile(content), "cards.csv");

    private List<(string GameCardId, int Qty, string Condition, int? LocationId, int? Page)> Lots()
    {
        using var ctx = new OmniCardDbContext(_opts);
        return ctx.Lots.Include(l => l.Product).AsEnumerable()
            .Select(l => (l.Product.GameCardId!, l.Quantity, l.Condition, l.LocationId, l.Page))
            .ToList();
    }

    private const string NativeHeader =
        "Game,GameCardId,Name,SetName,SetCode,Number,Rarity,Condition,IsFoil,FoilType,PurchasePrice,DateAdded,ContainerName,ContainerType,Page,Slot,Section,Quantity\n";

    [Fact]
    public void Csv_AllRowsValid_ImportIntoRouteLocation_IgnoringFileLocationAndPlacement()
    {
        var outcome = ImportCsv(NativeHeader +
            "Mtg,bolt-2x2,Lightning Bolt,Double Masters 2022,2X2,117,common,LP,False,,,2024-01-01T00:00:00Z,Blue Binder,Binder,3,4,,4\n" +
            "Mtg,sol-scd,Sol Ring,Starter Commander Decks,SCD,276,common,DMG,False,,,2024-01-01T00:00:00Z,,,,,,\n");

        Assert.Null(outcome.Failure);
        Assert.Equal((2, 5), (outcome.Result!.Lines, outcome.Result.Copies));
        Assert.Equal("OmniCard", outcome.Result.Format);
        var lots = Lots();
        Assert.All(lots, l => Assert.Equal((int?)_boxId, l.LocationId));
        Assert.All(lots, l => Assert.Null(l.Page));
        Assert.Contains(lots, l => l.GameCardId == "bolt-2x2" && l.Qty == 4 && l.Condition == "LP");
        Assert.Contains(lots, l => l.GameCardId == "sol-scd" && l.Qty == 1 && l.Condition == "DMG");

        using var ctx = new OmniCardDbContext(_opts);
        Assert.DoesNotContain(ctx.StorageContainers, c => c.Name == "Blue Binder");
    }

    [Fact]
    public void Csv_AnyBadRow_ImportsNothing_AndExplainsEveryProblemByRow()
    {
        var outcome = ImportCsv(
            "Count,Name,Edition,Collector Number,Condition,Foil,Purchase Price\n" +
            "4,Lightning Bolt,2X2,117,Near Mint,,\n" +       // row 2: fine
            "1,Nonexistent Card,ABC,1,Near Mint,,\n" +      // row 3: not in catalog
            "1,Sol Ring,SCD,276,Excellent,,\n" +            // row 4: unknown condition
            "two,Sol Ring,SCD,276,Near Mint,,\n");          // row 5: bad quantity

        Assert.Null(outcome.Result);
        Assert.Empty(Lots());
        var errors = outcome.Failure!.Errors;
        Assert.Equal([3, 4, 5], errors.Select(e => e.Row!.Value).ToArray());
        Assert.Contains("Nonexistent Card", errors[0].Card);
        Assert.Contains("wasn't found", errors[0].Message);
        Assert.Contains("Excellent", errors[1].Message);
        Assert.Contains("Count 'two'", errors[2].Message);
        Assert.Contains("3 problems found in 3 rows of 4", outcome.Failure.Error);
    }

    [Fact]
    public void Csv_UnknownCatalogId_Rejected()
    {
        var outcome = ImportCsv(NativeHeader +
            "Mtg,does-not-exist,Mystery,,,,,NM,False,,,,,,,,,\n");

        Assert.Empty(Lots());
        var error = Assert.Single(outcome.Failure!.Errors);
        Assert.Equal(2, error.Row);
        Assert.Contains("'does-not-exist'", error.Message);
    }

    [Fact]
    public void Csv_UnknownGame_ExplainsValidGames()
    {
        var outcome = ImportCsv(NativeHeader + "Magic,bolt-2x2,Lightning Bolt,,,,,NM,False,,,,,,,,,\n");

        var error = Assert.Single(outcome.Failure!.Errors);
        Assert.Contains("Unknown game 'Magic'", error.Message);
        Assert.Contains("Mtg", error.Message);
    }

    [Fact]
    public void Csv_UnrecognizedHeaders_Rejected_ListingFoundColumns()
    {
        var outcome = ImportCsv("Card,Qty\nLightning Bolt,1\n");

        Assert.Contains("recognized card CSV format", outcome.Failure!.Error);
        Assert.Contains("Card, Qty", Assert.Single(outcome.Failure.Errors).Message);
    }

    [Fact]
    public void Csv_HeaderOnly_Rejected()
    {
        Assert.Contains("no cards", ImportCsv(NativeHeader).Failure!.Error);
    }

    [Fact]
    public void Csv_TcgPlayerRows_ResolveBySetNameAndNumber_WithQuantity()
    {
        var outcome = ImportCsv(
            "Quantity,Name,Set Name,Number,Condition,Printing,Price\n" +
            "3,Lightning Bolt,Magic 2010,146,Lightly Played,Normal,1.00\n");

        Assert.Null(outcome.Failure);
        var lot = Assert.Single(Lots());
        Assert.Equal(("bolt-m10", 3, "LP"), (lot.GameCardId, lot.Qty, lot.Condition));
    }

    [Fact]
    public void Csv_PrintingNotInCatalog_ImportsAnotherPrintingOfTheName_AndReportsIt()
    {
        var outcome = ImportCsv(
            "Count,Name,Edition,Collector Number,Condition,Foil,Purchase Price\n" +
            "1,Lightning Bolt,ZZZ,999,Near Mint,,\n");

        var sub = Assert.Single(outcome.Result!.Substitutions);
        Assert.Equal(2, sub.Row);
        Assert.Contains("M10 #146", sub.Message);
        Assert.Equal("bolt-m10", Assert.Single(Lots()).GameCardId);
    }

    [Fact]
    public void Csv_OtherGameIntoGameLockedDeckBox_Rejected()
    {
        var deckBox = NewLocation("Pikachu Deck", ContainerType.DeckBox, CardGame.Pokemon);

        var outcome = ImportCsv(NativeHeader + "Mtg,bolt-2x2,Lightning Bolt,,,,,NM,False,,,,,,,,,\n", deckBox);

        Assert.Empty(Lots());
        Assert.Contains("\"Pikachu Deck\" is a Pokémon deck box", Assert.Single(outcome.Failure!.Errors).Message);
    }

    [Fact]
    public void Csv_NeverSkipsCopiesAlreadyInCollection()
    {
        const string csv = NativeHeader + "Mtg,sol-scd,Sol Ring,,SCD,276,,NM,False,,,,,,,,,\n";
        ImportCsv(csv);
        ImportCsv(csv);

        Assert.Equal(2, Lots().Count);
    }

    [Fact]
    public void MissingLocation_NotFound()
    {
        Assert.True(ImportCsv(NativeHeader, locationId: 9999).LocationNotFound);
    }

    [Fact]
    public async Task Url_AllResolved_ImportsIntoLocation_MergingRepeats()
    {
        _decklists.FetchResult = ("My Deck",
        [
            new DecklistEntry(2, "Lightning Bolt", "2X2", "117"),
            new DecklistEntry(2, "Lightning Bolt", "2X2", "117"),
            new DecklistEntry(1, "Sol Ring", "SCD", "276", "Etched"),
        ]);

        var outcome = await _service.ImportUrlAsync(_boxId, new LocationUrlImportRequest { Url = "https://moxfield.com/decks/abc", Condition = "LP" });

        Assert.Equal(("My Deck", 2, 5), (outcome.Result!.Source, outcome.Result.Lines, outcome.Result.Copies));
        Assert.Contains(Lots(), l => l.GameCardId == "bolt-2x2" && l.Qty == 4 && l.Condition == "LP" && l.LocationId == _boxId);
    }

    [Fact]
    public async Task Url_AnyUnresolvedCard_ImportsNothing_AndNamesIt()
    {
        _decklists.FetchResult = ("Deck",
        [
            new DecklistEntry(1, "Sol Ring", "SCD", "276"),
            new DecklistEntry(1, "Nonexistent Card", "ABC", "1"),
        ]);

        var outcome = await _service.ImportUrlAsync(_boxId, new LocationUrlImportRequest { Url = "https://archidekt.com/decks/123" });

        Assert.Empty(Lots());
        var error = Assert.Single(outcome.Failure!.Errors);
        Assert.Equal("Nonexistent Card", error.Card);
        Assert.Contains("(ABC #1)", error.Message);
    }

    [Fact]
    public async Task Url_NotADeckUrl_OrUnreachable_Explained()
    {
        var notDeck = await _service.ImportUrlAsync(_boxId, new LocationUrlImportRequest { Url = "https://example.com/decks/1" });
        Assert.Contains("isn't a Moxfield or Archidekt deck URL", notDeck.Failure!.Error);

        _decklists.FetchResult = null;
        var unreachable = await _service.ImportUrlAsync(_boxId, new LocationUrlImportRequest { Url = "https://moxfield.com/decks/abc" });
        Assert.Contains("Couldn't download that deck", unreachable.Failure!.Error);
    }

    [Fact]
    public async Task Url_IntoNonMagicDeckBox_Rejected()
    {
        var deckBox = NewLocation("Pikachu Deck", ContainerType.DeckBox, CardGame.Pokemon);
        _decklists.FetchResult = ("Deck", [new DecklistEntry(1, "Sol Ring", "SCD", "276")]);

        var outcome = await _service.ImportUrlAsync(deckBox, new LocationUrlImportRequest { Url = "https://moxfield.com/decks/abc" });

        Assert.Contains("Pokémon deck box", outcome.Failure!.Error);
        Assert.Empty(Lots());
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
        public DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries, CardGame game) => throw new NotImplementedException();
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
