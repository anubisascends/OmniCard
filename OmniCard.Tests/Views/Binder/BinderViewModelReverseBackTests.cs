using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Binder;
using Xunit;

namespace OmniCard.Tests.Views.Binder;

/// <summary>
/// Regression coverage for the desktop binder editor's reverse-side card-back hints surviving page
/// mutations. The bug: <see cref="BinderViewModel"/> cached the sheet layout only at <c>Load</c>, so
/// after adding a page and assigning a card to it, the empty pocket on the reverse side never lit up
/// its card back (the cached layout didn't know the new sheet existed). The fix re-reads the layout
/// inside <c>Refresh</c>. Uses a real <see cref="StorageContainerService"/> over in-memory SQLite,
/// same pattern as the other DB-backed service tests.
/// </summary>
public class BinderViewModelReverseBackTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly StorageContainerService _containers;
    private readonly BinderViewModel _vm;
    private readonly int _binderId;

    public BinderViewModelReverseBackTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();

        var factory = new MockFactory(_opts);
        _containers = new StorageContainerService(factory);
        _binderId = _containers.Create("Binder A", ContainerType.Binder).Id;

        var cardService = new Mock<ICardService>();
        cardService
            .Setup(s => s.GetUnplacedBinderCards(It.IsAny<int>(), It.IsAny<FilterPreset?>()))
            .Returns(new List<CollectionCard>());

        var tagService = new Mock<ITagService>();
        tagService
            .Setup(s => s.GetTagsByLots(It.IsAny<IEnumerable<int>>()))
            .Returns(new Dictionary<int, List<string>>());

        _vm = new BinderViewModel(
            _containers,
            cardService.Object,
            Mock.Of<IDialogService>(),
            Mock.Of<IListingService>(),
            tagService.Object,
            Mock.Of<IEbayListingService>(),
            Options.Create(new EbaySettings()),
            Mock.Of<IDataPathService>());
    }

    public void Dispose() => _conn.Dispose();

    private int AddLot(int page, int slot)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var product = new Product
        {
            Game = CardGame.Pokemon,
            Category = ProductCategory.Single,
            GameCardId = $"card-p{page}s{slot}",
            Name = $"Card p{page} s{slot}",
            SetCode = "SET",
            SetName = "Set Name",
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = product.Id, LocationId = _binderId, Condition = "NM" };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    [Fact]
    public void AddPage_ThenAssignCard_LightsReverseCardBackOnNewSheet()
    {
        _vm.Load(_binderId);

        // Add a fresh double-sided sheet (its two logical pages become the reverse of each other),
        // then place a card into one of its pockets via the same command the drop handler uses.
        _vm.AddPage("double");
        var newSheetSecondPage = _vm.TotalPages;          // trailing page of the just-added sheet
        var newSheetFirstPage = newSheetSecondPage - 1;   // its reverse side within the same sheet

        var lotId = AddLot(newSheetSecondPage, slot: 0);
        _vm.DropOnSlot(lotId, newSheetSecondPage, slot: 0);

        // Navigate so the reverse page (newSheetFirstPage) is on screen, then find its empty pocket
        // whose mirror on the reverse holds the card we just placed.
        var mirror = CardBackAssets.MirrorSlot(0, _vm.Columns, _vm.SlotsPerPage);
        Assert.NotNull(mirror);

        _vm.SpreadIndex = newSheetFirstPage / 2; // page P lives on spread P/2 (left) or P/2 with pairing
        var onScreen = _vm.LeftPageSlots.Concat(_vm.RightPageSlots).ToList();

        var pocket = onScreen.SingleOrDefault(s => s.Page == newSheetFirstPage && s.SlotIndex == mirror);
        Assert.NotNull(pocket);
        Assert.True(pocket!.HasCardOnReverse, "Empty pocket on the new sheet should show the reverse card back.");
        Assert.Equal(CardGame.Pokemon, pocket.ReverseGame);
    }

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
