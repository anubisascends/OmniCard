using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Collection.Inventory;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Settings;
using OmniCard.Shared.Storage;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>The printed language of an owned copy lives on the lot: persisted by scan commit (null for
/// English), surfaced on the collection DTO, editable, carried by splits, and searchable with lang:.</summary>
public class LotLanguageTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly IDbContextFactory<OmniCardDbContext> _factory;
    private readonly WebBinderCardService _service;
    private readonly int _boxId;

    public LotLanguageTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();
        _factory = new Factory(_opts);
        _service = new WebBinderCardService(_factory, new StubDataPath());
        _boxId = new StorageContainerService(_factory).Create("Box A", ContainerType.Box).Id;
    }

    public void Dispose() => _conn.Dispose();

    private CollectionCard Scan(string gameCardId, string language, int quantity = 1) => new()
    {
        Game = CardGame.Mtg, GameCardId = gameCardId, Name = "Ancestral Recall", SetCode = "neo", SetName = "Kamigawa",
        Number = "1", Rarity = "rare", Condition = "NM", Language = language, Quantity = quantity, ContainerId = _boxId,
    };

    [Fact]
    public void AddScannedLots_PersistsLanguage_EnglishAsNull()
    {
        var ids = _service.AddScannedLots([Scan("ja-id", "ja"), Scan("en-id", "en")]);

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal("ja", ctx.Lots.Single(l => l.Id == ids[0]).Language);
        Assert.Null(ctx.Lots.Single(l => l.Id == ids[1]).Language);

        var cards = _service.GetCollectionCards(ids);
        Assert.Equal("ja", cards.Single(c => c.Id == ids[0]).Language);
        Assert.Equal("en", cards.Single(c => c.Id == ids[1]).Language);
    }

    [Fact]
    public void UpdateCollectionCard_ChangesLanguage()
    {
        var id = _service.AddScannedLots([Scan("en-id", "en")])[0];

        var card = _service.GetCollectionCards([id]).Single();
        card.Language = "de";
        _service.UpdateCollectionCard(card);

        Assert.Equal("de", _service.GetCollectionCards([id]).Single().Language);
    }

    [Fact]
    public void SplitStack_CarriesLanguage()
    {
        var id = _service.AddScannedLots([Scan("ja-id", "ja", quantity: 3)])[0];

        var splitId = _service.SplitStack(id, 1);

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal("ja", ctx.Lots.Single(l => l.Id == splitId).Language);
    }

    [Theory]
    [InlineData("lang:ja", 1)]
    [InlineData("lang:japanese", 1)]
    [InlineData("lang:en", 1)]
    [InlineData("-lang:en", 1)]
    [InlineData("lang:de", 0)]
    public void CollectionQuery_LangFilter(string query, int expected)
    {
        _service.AddScannedLots([Scan("ja-id", "ja"), Scan("en-id", "en")]);

        using var ctx = new OmniCardDbContext(_opts);
        var results = CollectionQueryBuilder.BuildFilteredQuery(ctx, query, null, null, null).ToList();

        Assert.Equal(expected, results.Count);
    }

    [Fact]
    public void AuditReconcile_OverwritesLanguageFromScan()
    {
        var id = _service.AddScannedLots([Scan("en-id", "en")])[0];

        var result = _service.ReconcileLocationAudit(_boxId, [Scan("en-id", "ja")]);

        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal("ja", _service.GetCollectionCards([id]).Single().Language);
    }

    [Fact]
    public void ImportCollectionCards_SkipDuplicates_TreatsLanguagesAsDistinctCopies()
    {
        _service.ImportCollectionCards([Scan("same-id", "en")], skipDuplicates: true);

        Assert.Equal(1, _service.ImportCollectionCards([Scan("same-id", "ja")], skipDuplicates: true));
        Assert.Equal(0, _service.ImportCollectionCards([Scan("same-id", "ja")], skipDuplicates: true));
    }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
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
