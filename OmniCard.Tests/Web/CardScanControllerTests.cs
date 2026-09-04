using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Api.Contracts;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web.Api;
using OmniCard.Web.Services;
using Xunit;

namespace OmniCard.Tests.Web;

/// <summary>
/// Covers the SPA's server-side scan endpoints: upload validation, catalog correction search, and the
/// commit-to-location write. The matching pipeline itself (pHash/OCR) is exercised by the desktop
/// matching tests and verified live against real card art; these tests pin the HTTP contract.
/// </summary>
public class CardScanControllerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly WebBinderCardService _binderCards;
    private readonly StorageContainerService _containers;
    private readonly Mock<ICardService> _cardService = new();

    public CardScanControllerTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();

        var factory = new MockFactory(_opts);
        _binderCards = new WebBinderCardService(factory, new StubDataPath());
        _containers = new StorageContainerService(factory);
    }

    public void Dispose() => _conn.Dispose();

    // Match is constructed with a null matcher for the validation cases below — they all short-circuit
    // before the matcher is ever invoked (no image / bad type / oversized / unknown game).
    private CardScanController CreateController() =>
        new(matcher: null!, _cardService.Object, _binderCards, NullLogger<CardScanController>.Instance);

    private static IFormFile CreateFormFile(byte[]? content = null, string contentType = "image/jpeg",
        string fileName = "test.jpg", long? overrideLength = null)
    {
        content ??= [0xFF, 0xD8, 0xFF, 0xE0];
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, overrideLength ?? content.Length, "image", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    [Fact]
    public async Task Match_NoImage_Returns400()
    {
        var result = await CreateController().Match(null!, "Mtg", false, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Match_WrongContentType_Returns400()
    {
        var file = CreateFormFile(contentType: "text/plain", fileName: "x.txt");
        var result = await CreateController().Match(file, "Mtg", false, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Match_Oversized_Returns400()
    {
        var file = CreateFormFile(content: [0xFF], overrideLength: 11 * 1024 * 1024);
        var result = await CreateController().Match(file, "Mtg", false, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Match_UnknownGame_Returns400()
    {
        var file = CreateFormFile(new byte[1024]);
        var result = await CreateController().Match(file, "Nonsense", false, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void Search_BlankQuery_ReturnsEmptyWithoutHittingGameService()
    {
        var result = CreateController().Search("Mtg", "   ");
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<ScanSearchResultDto>>(ok.Value));
        _cardService.Verify(c => c.GetGameService(It.IsAny<CardGame>()), Times.Never);
    }

    [Fact]
    public void Search_UnknownGame_Returns400()
    {
        var result = CreateController().Search("Nonsense", "bolt");
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void Search_MapsGameServiceResults()
    {
        var game = new Mock<ICardGameService>();
        game.Setup(s => s.SearchCards("bolt", 20)).Returns([
            new CardMatch { Name = "Lightning Bolt", SetCode = "lea", SetName = "Alpha",
                CollectorNumber = "161", Rarity = "common", GameSpecificId = "abc", ImageUri = "u" }
        ]);
        _cardService.Setup(c => c.GetGameService(CardGame.Mtg)).Returns(game.Object);

        var result = CreateController().Search("Mtg", "bolt");
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IReadOnlyList<ScanSearchResultDto>>(ok.Value);
        Assert.Single(list);
        Assert.Equal("Lightning Bolt", list[0].Name);
        Assert.Equal("abc", list[0].GameCardId);
    }

    [Fact]
    public void Commit_NoLocation_Returns400()
    {
        var req = new ScanCommitRequest { ContainerId = 0, Items = [new ScanCommitItem { Game = "Mtg", GameCardId = "x" }] };
        Assert.IsType<BadRequestObjectResult>(CreateController().Commit(req).Result);
    }

    [Fact]
    public void Commit_NoItems_Returns400()
    {
        var req = new ScanCommitRequest { ContainerId = 1, Items = [] };
        Assert.IsType<BadRequestObjectResult>(CreateController().Commit(req).Result);
    }

    [Fact]
    public void Commit_UnknownGameInItem_Returns400()
    {
        var loc = _containers.Create("Box", ContainerType.Box).Id;
        var req = new ScanCommitRequest { ContainerId = loc, Items = [new ScanCommitItem { Game = "Nope", GameCardId = "x" }] };
        Assert.IsType<BadRequestObjectResult>(CreateController().Commit(req).Result);
    }

    [Fact]
    public void Commit_WritesLotsToLocation()
    {
        var loc = _containers.Create("Box", ContainerType.Box).Id;
        var req = new ScanCommitRequest
        {
            ContainerId = loc,
            Items = [
                new ScanCommitItem { Game = "Mtg", GameCardId = "id-1", Name = "Bolt", SetCode = "lea",
                    SetName = "Alpha", CollectorNumber = "161", Rarity = "common", Condition = "NM", Quantity = 2 },
                new ScanCommitItem { Game = "Pokemon", GameCardId = "id-2", Name = "Pikachu", SetCode = "base",
                    SetName = "Base", CollectorNumber = "58", Rarity = "common", IsFoil = true },
            ],
        };

        var ok = Assert.IsType<OkObjectResult>(CreateController().Commit(req).Result);
        Assert.Equal(2, Assert.IsType<ScanCommitResultDto>(ok.Value).Imported);

        using var ctx = new OmniCardDbContext(_opts);
        var lots = ctx.Lots.Where(l => l.LocationId == loc).ToList();
        Assert.Equal(2, lots.Count);
        Assert.Contains(lots, l => l.Quantity == 2);
    }

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
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
