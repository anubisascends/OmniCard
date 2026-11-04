using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Settings;
using OmniCard.Shared.Storage;
using OmniCard.Shared.Tags;
using OmniCard.Tests.Fakes;
using OmniCard.Web.Services;
using OmniCard.Web.Services.BuyList;

namespace OmniCard.Tests.Web.BuyLists;

/// <summary>An in-memory unified store with the Buy List and scan-commit services wired to it.</summary>
public sealed class BuyListFixture : IDisposable
{
    private readonly SqliteConnection _conn;

    public DbContextOptions<OmniCardDbContext> Options { get; }
    public IDbContextFactory<OmniCardDbContext> Factory { get; }
    public FakeBuyListSettings Settings { get; } = new();
    public BuyListService Service { get; }
    public ScanCommitService Commits { get; }
    public Mock<ICardService> CardService { get; } = new();

    public BuyListFixture()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        Options = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(Options)) ctx.Database.EnsureCreated();
        Factory = new OptionsFactory(Options);
        Service = BuyListServices.Create(Factory, Settings);

        CardService.Setup(c => c.GetGameService(It.IsAny<CardGame>())).Returns(new Mock<ICardGameService>().Object);
        var paths = new Mock<IDataPathService>();
        paths.SetupGet(p => p.DataDirectory).Returns(Path.GetTempPath());
        paths.SetupGet(p => p.ScansDirectory).Returns(Path.GetTempPath());
        Commits = new ScanCommitService(CardService.Object, new WebBinderCardService(Factory, paths.Object),
            new Mock<ITagService>().Object, Service, NullLogger<ScanCommitService>.Instance);
    }

    public void Dispose() => _conn.Dispose();

    public BuyListEntryDto Want(string name, string cardId = "printing-1", int quantity = 1, bool pin = false, bool? foil = null,
        string priority = "Normal", string? note = null, string game = "Mtg") =>
        Service.Add(new BuyListEntryCreateRequest
        {
            Game = game, Name = name, GameCardId = cardId, SetCode = "tst", SetName = "Test Set", CollectorNumber = "1",
            ImageUri = "http://img/" + cardId, PinPrinting = pin, Foil = foil, WantedQuantity = quantity,
            Priority = priority, Note = note,
        }, "admin");

    public int Box(string name, ContainerType type = ContainerType.Box, CardGame? game = null)
    {
        using var ctx = Factory.CreateDbContext();
        var box = new StorageContainer { Name = name, ContainerType = type, Game = game };
        ctx.StorageContainers.Add(box);
        ctx.SaveChanges();
        return box.Id;
    }

    public static ScanCommitItem Scan(string name, string cardId = "printing-1", int quantity = 1, bool foil = false) => new()
    {
        Game = "Mtg", GameCardId = cardId, Name = name, SetCode = "tst", SetName = "Test Set", CollectorNumber = "1",
        Quantity = quantity, IsFoil = foil,
    };

    private sealed class OptionsFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
