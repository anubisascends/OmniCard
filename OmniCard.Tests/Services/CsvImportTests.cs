using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Interfaces;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class CsvImportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<CollectionDbContext> _dbFactory;

    public CsvImportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbFactory = new TestCollectionDbFactory(options);
        using var ctx = _dbFactory.CreateDbContext();
        ctx.Database.EnsureCreated();

        // Seed Bulk container for imports
        ctx.StorageContainers.Add(new StorageContainer
        {
            Name = "Bulk",
            ContainerType = ContainerType.Bulk,
            IsSystem = true,
            SortOrder = 0,
        });
        ctx.SaveChanges();
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private CsvExportImportService CreateService(IStorageContainerService? containerService = null)
    {
        return new CsvExportImportService(
            _dbFactory,
            null!, // IScryfallService — not needed for app-native import tests
            containerService ?? new StubContainerService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CsvExportImportService>.Instance);
    }

    private string WriteCsv(string filename, string content)
    {
        var path = Path.Combine(_tempDir, filename);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void PreviewImport_DetectsAppNativeFormat()
    {
        var path = WriteCsv("native.csv",
            "Game,GameCardId,Name,SetName,SetCode,Number,Rarity,Condition,IsFoil,PurchasePrice,DateAdded,ContainerName,ContainerType,Page,Slot,Section\n" +
            "Mtg,abc-123,Lightning Bolt,Alpha,LEA,161,common,NM,False,5.99,2026-01-15T00:00:00.0000000Z,,,,\n");

        var preview = CreateService().PreviewImport(path);
        Assert.Equal(CsvFormat.AppNative, preview.DetectedFormat);
        Assert.Single(preview.Cards);
        Assert.Equal("Lightning Bolt", preview.Cards[0].Name);
        Assert.Equal(CardGame.Mtg, preview.Cards[0].Game);
    }

    [Fact]
    public void PreviewImport_DetectsTcgPlayerFormat()
    {
        var path = WriteCsv("tcg.csv",
            "Quantity,Name,Set Name,Number,Condition,Printing,Price\n" +
            "1,Lightning Bolt,Alpha,161,Near Mint,Normal,5.99\n");

        var preview = CreateService().PreviewImport(path);
        Assert.Equal(CsvFormat.TcgPlayer, preview.DetectedFormat);
    }

    [Fact]
    public void PreviewImport_DetectsMoxfieldFormat()
    {
        var path = WriteCsv("mox.csv",
            "Count,Name,Edition,Collector Number,Condition,Foil,Purchase Price\n" +
            "1,Lightning Bolt,LEA,161,NM,,5.99\n");

        var preview = CreateService().PreviewImport(path);
        Assert.Equal(CsvFormat.Moxfield, preview.DetectedFormat);
    }

    [Fact]
    public void PreviewImport_DetectsManaboxFormat()
    {
        var path = WriteCsv("manabox.csv",
            "Card Name,Set Code,Set Name,Collector Number,Rarity,Language,Quantity,Condition,Finish,Altered,Signed,Misprint,Price (USD),Price (EUR),Price (USD Foil),Price (EUR Foil),Price (USD Etched),Price (EUR Etched),Scryfall ID,Container Type,Container Name\n" +
            "Lightning Bolt,LEA,Alpha,161,common,en,1,NM,nonfoil,false,false,false,5.99,,,,,,abc-123,list,recent\n");

        var preview = CreateService().PreviewImport(path);
        Assert.Equal(CsvFormat.Manabox, preview.DetectedFormat);
        Assert.Single(preview.Cards);
        Assert.Equal("abc-123", preview.Cards[0].GameCardId);
    }

    [Fact]
    public void PreviewImport_AppNative_ParsesLocationData()
    {
        var path = WriteCsv("loc.csv",
            "Game,GameCardId,Name,SetName,SetCode,Number,Rarity,Condition,IsFoil,PurchasePrice,DateAdded,ContainerName,ContainerType,Page,Slot,Section\n" +
            "Mtg,abc-123,Lightning Bolt,Alpha,LEA,161,common,NM,False,,2026-01-15T00:00:00.0000000Z,Red Binder,Binder,3,7,\n");

        var preview = CreateService().PreviewImport(path);
        Assert.Equal("Red Binder", preview.Cards[0].Container?.Name);
        Assert.Equal(3, preview.Cards[0].Page);
        Assert.Equal(7, preview.Cards[0].Slot);
    }

    [Fact]
    public void ImportCards_AddsCardsToDatabase()
    {
        var path = WriteCsv("import.csv",
            "Game,GameCardId,Name,SetName,SetCode,Number,Rarity,Condition,IsFoil,PurchasePrice,DateAdded,ContainerName,ContainerType,Page,Slot,Section\n" +
            "Mtg,abc-123,Lightning Bolt,Alpha,LEA,161,common,NM,False,5.99,2026-01-15T00:00:00.0000000Z,,,,\n");

        var svc = CreateService();
        var preview = svc.PreviewImport(path);
        var count = svc.ImportCards(preview, skipDuplicates: false);

        Assert.Equal(1, count);

        using var ctx = _dbFactory.CreateDbContext();
        var card = ctx.Cards.First();
        Assert.Equal("Lightning Bolt", card.Name);
        Assert.Equal("abc-123", card.GameCardId);
        Assert.Equal(5.99m, card.PurchasePrice);
    }

    [Fact]
    public void ImportCards_SkipsDuplicates()
    {
        // Seed a card
        using (var ctx = _dbFactory.CreateDbContext())
        {
            ctx.Cards.Add(new CollectionCard
            {
                Game = CardGame.Mtg,
                GameCardId = "abc-123",
                Name = "Lightning Bolt",
                SetName = "Alpha",
                SetCode = "LEA",
                Number = "161",
                Rarity = "common",
                Condition = "NM",
                IsFoil = false,
            });
            ctx.SaveChanges();
        }

        var path = WriteCsv("dup.csv",
            "Game,GameCardId,Name,SetName,SetCode,Number,Rarity,Condition,IsFoil,PurchasePrice,DateAdded,ContainerName,ContainerType,Page,Slot,Section\n" +
            "Mtg,abc-123,Lightning Bolt,Alpha,LEA,161,common,NM,False,,,,,,,\n");

        var svc = CreateService();
        var preview = svc.PreviewImport(path);
        var count = svc.ImportCards(preview, skipDuplicates: true);

        Assert.Equal(0, count);
    }

    [Fact]
    public void ImportCards_AddsDuplicatesWhenNotSkipping()
    {
        // Seed a card
        using (var ctx = _dbFactory.CreateDbContext())
        {
            ctx.Cards.Add(new CollectionCard
            {
                Game = CardGame.Mtg,
                GameCardId = "abc-123",
                Name = "Lightning Bolt",
                SetName = "Alpha",
                SetCode = "LEA",
                Number = "161",
                Rarity = "common",
                Condition = "NM",
                IsFoil = false,
            });
            ctx.SaveChanges();
        }

        var path = WriteCsv("dup2.csv",
            "Game,GameCardId,Name,SetName,SetCode,Number,Rarity,Condition,IsFoil,PurchasePrice,DateAdded,ContainerName,ContainerType,Page,Slot,Section\n" +
            "Mtg,abc-123,Lightning Bolt,Alpha,LEA,161,common,NM,False,,,,,,,\n");

        var svc = CreateService();
        var preview = svc.PreviewImport(path);
        var count = svc.ImportCards(preview, skipDuplicates: false);

        Assert.Equal(1, count);

        using var ctx2 = _dbFactory.CreateDbContext();
        Assert.Equal(2, ctx2.Cards.Count());
    }

    [Fact]
    public void PreviewImport_ManaboxFinish_MapsFoilCorrectly()
    {
        var path = WriteCsv("foil.csv",
            "Card Name,Set Code,Set Name,Collector Number,Rarity,Language,Quantity,Condition,Finish,Altered,Signed,Misprint,Price (USD),Price (EUR),Price (USD Foil),Price (EUR Foil),Price (USD Etched),Price (EUR Etched),Scryfall ID,Container Type,Container Name\n" +
            "Lightning Bolt,LEA,Alpha,161,common,en,1,NM,foil,false,false,false,,,,,,,abc-123,list,recent\n");

        var preview = CreateService().PreviewImport(path);
        Assert.True(preview.Cards[0].IsFoil);
    }

    [Fact]
    public void PreviewImport_TcgPlayer_MapsConditionCorrectly()
    {
        var path = WriteCsv("tcg_cond.csv",
            "Quantity,Name,Set Name,Number,Condition,Printing,Price\n" +
            "1,Lightning Bolt,Alpha,161,Lightly Played,Foil,3.50\n");

        var preview = CreateService().PreviewImport(path);
        Assert.Single(preview.Cards);
        Assert.Equal("LP", preview.Cards[0].Condition);
        Assert.True(preview.Cards[0].IsFoil);
        Assert.Equal(3.50m, preview.Cards[0].PurchasePrice);
    }

    private class TestCollectionDbFactory(DbContextOptions<CollectionDbContext> options) : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }

    private class StubContainerService : IStorageContainerService
    {
        private readonly List<StorageContainer> _containers =
        [
            new() { Id = 1, Name = "Bulk", ContainerType = ContainerType.Bulk, IsSystem = true, SortOrder = 0 }
        ];
        private int _nextId = 2;

        public List<StorageContainer> GetAll() => _containers;
        public StorageContainer GetBulk() => _containers.First(c => c.IsSystem);
        public StorageContainer Create(string name, ContainerType type)
        {
            var c = new StorageContainer { Id = _nextId++, Name = name, ContainerType = type };
            _containers.Add(c);
            return c;
        }
        public void Rename(int id, string newName) { }
        public void Delete(int id, bool moveCardsToBulk = true) { }
        public int GetCardCount(int containerId) => 0;
        public void SetCoverCard(int containerId, int? cardId) { }
        public List<CollectionCard> GetCardsInContainer(int containerId) => [];
    }
}
