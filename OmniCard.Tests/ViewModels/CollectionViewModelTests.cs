using System.Collections.ObjectModel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OmniCard.Collection;
using OmniCard.Controls;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Root;
using Xunit;

namespace OmniCard.Tests.ViewModels;

public class CollectionViewModelTests
{
    private readonly Mock<ICardService> _card = new();
    private readonly Mock<IStorageContainerService> _containers = new();
    private readonly Mock<ICollectionPresetService> _presets = new();
    private readonly Mock<IDialogService> _dialog = new();
    private readonly Mock<ICollectionQueryService> _query = new();
    private readonly Mock<IDataPathService> _dataPath = new();
    private readonly Mock<IEbayListingService> _ebayListing = new();
    private readonly Mock<IListingService> _listing = new();
    private readonly Mock<ITagService> _tags = new();

    private CollectionViewModel CreateVm()
    {
        // Preset lookups are iterated, so they must return real (empty) lists, not null.
        _presets.Setup(p => p.GetSortPresets(It.IsAny<CardGame>())).Returns([]);
        _presets.Setup(p => p.GetFilterPresets(It.IsAny<CardGame>())).Returns([]);
        _containers.Setup(c => c.GetAll()).Returns([]);
        _query.Setup(q => q.GetLocationOverviewsAsync(It.IsAny<CardGame?>()))
              .ReturnsAsync([]);
        // Card-list search path with an empty result set: count 0, no rows added, empty status map.
        _card.Setup(c => c.GetSearchCount(It.IsAny<string>(), It.IsAny<CardGame?>(), It.IsAny<int?>(),
                                          It.IsAny<FilterPreset?>(), It.IsAny<bool>()))
             .Returns(0);
        _listing.Setup(l => l.GetActiveListingStatusByLot(It.IsAny<IEnumerable<int>>()))
                .Returns(new Dictionary<int, ListingStatus>());
        _tags.Setup(t => t.GetTagsByLots(It.IsAny<IEnumerable<int>>()))
             .Returns(new Dictionary<int, List<string>>());

        return new CollectionViewModel(
            _card.Object,
            _containers.Object,
            _presets.Object,
            _dialog.Object,
            _query.Object,
            Options.Create(new DisplaySettings()),
            _dataPath.Object,
            NullLogger<CollectionViewModel>.Instance,
            _ebayListing.Object,
            Options.Create(new EbaySettings()),
            _listing.Object,
            _tags.Object);
    }

    [Fact]
    public async Task SetGame_InOverviewMode_ReloadsOverviewForNewGame()
    {
        var vm = CreateVm();          // ShowCardList defaults to false (overview mode)
        _query.Invocations.Clear();   // ignore any construction-time calls

        vm.SetGame(CardGame.OnePiece);
        await Task.Yield();           // let the fire-and-forget overview load run

        _query.Verify(q => q.GetLocationOverviewsAsync(CardGame.OnePiece), Times.Once);
    }

    [Fact]
    public async Task SetGame_InCardListMode_ReSearchesForNewGame()
    {
        var vm = CreateVm();
        vm.ShowCardList = true;       // simulate viewing the card list

        // The card-list search runs its DB query inside Task.Run (a background thread), so a bare
        // yield can't guarantee it has executed before Verify. Signal from the mock and wait for it.
        var searched = new TaskCompletionSource();
        _card.Setup(c => c.SearchCollection(
                It.IsAny<string>(), It.IsAny<CardGame>(), It.IsAny<int?>(),
                It.IsAny<SortPreset?>(), It.IsAny<FilterPreset?>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<ObservableCollection<CollectionCard>>()))
             .Callback(() => searched.TrySetResult());
        _card.Invocations.Clear();

        vm.SetGame(CardGame.OnePiece);
        await searched.Task.WaitAsync(TimeSpan.FromSeconds(5)); // deterministic: throws if never called

        _card.Verify(c => c.SearchCollection(
            It.IsAny<string>(), CardGame.OnePiece, It.IsAny<int?>(),
            It.IsAny<SortPreset?>(), It.IsAny<FilterPreset?>(), It.IsAny<bool>(),
            0, It.IsAny<int>(), It.IsAny<ObservableCollection<CollectionCard>>()), Times.Once);
    }

    [Fact]
    public async Task SetGame_AllGames_SearchesWithNullGameFilter()
    {
        var vm = CreateVm();
        vm.ShowCardList = true;

        var searched = new TaskCompletionSource();
        _card.Setup(c => c.SearchCollection(
                It.IsAny<string>(), It.IsAny<CardGame?>(), It.IsAny<int?>(),
                It.IsAny<SortPreset?>(), It.IsAny<FilterPreset?>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<ObservableCollection<CollectionCard>>()))
             .Callback(() => searched.TrySetResult());
        _card.Invocations.Clear();

        vm.SetGame(null); // All Games
        await searched.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _card.Verify(c => c.SearchCollection(
            It.IsAny<string>(), (CardGame?)null, It.IsAny<int?>(),
            It.IsAny<SortPreset?>(), It.IsAny<FilterPreset?>(), It.IsAny<bool>(),
            0, It.IsAny<int>(), It.IsAny<ObservableCollection<CollectionCard>>()), Times.Once);
    }

    [Fact]
    public async Task BrowseSet_FiltersToGameAndSet()
    {
        var vm = CreateVm();

        var searched = new TaskCompletionSource();
        _card.Setup(c => c.SearchCollection(
                It.IsAny<string>(), It.IsAny<CardGame?>(), It.IsAny<int?>(),
                It.IsAny<SortPreset?>(), It.IsAny<FilterPreset?>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<ObservableCollection<CollectionCard>>()))
             .Callback(() => searched.TrySetResult());
        _card.Invocations.Clear();

        vm.BrowseSet(CardGame.OnePiece, "OP01");
        await searched.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(vm.ShowCardList);
        Assert.Equal("set:OP01", vm.CollectionSearchQuery);
        _card.Verify(c => c.SearchCollection(
            "set:OP01", CardGame.OnePiece, It.IsAny<int?>(),
            It.IsAny<SortPreset?>(), It.IsAny<FilterPreset?>(), It.IsAny<bool>(),
            0, It.IsAny<int>(), It.IsAny<ObservableCollection<CollectionCard>>()), Times.Once);
    }

    [Fact]
    public async Task BrowseSet_DoesNotWipeDrilledGamesActivePresets()
    {
        var sortPreset = new SortPreset { Name = "MySort", Game = CardGame.OnePiece, SortLevels = [] };
        var filterPreset = new FilterPreset { Name = "MyFilter", Game = CardGame.OnePiece };
        _presets.Setup(p => p.GetSortPresets(CardGame.OnePiece)).Returns([sortPreset]);
        _presets.Setup(p => p.GetFilterPresets(CardGame.OnePiece)).Returns([filterPreset]);
        _presets.Setup(p => p.GetActiveSortPreset(CardGame.OnePiece)).Returns(sortPreset);
        _presets.Setup(p => p.GetActiveFilterPreset(CardGame.OnePiece)).Returns(filterPreset);

        var vm = CreateVm();

        var searched = new TaskCompletionSource();
        _card.Setup(c => c.SearchCollection(
                It.IsAny<string>(), It.IsAny<CardGame?>(), It.IsAny<int?>(),
                It.IsAny<SortPreset?>(), It.IsAny<FilterPreset?>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<ObservableCollection<CollectionCard>>()))
             .Callback(() => searched.TrySetResult());
        _presets.Invocations.Clear();

        vm.BrowseSet(CardGame.OnePiece, "OP01");   // dashboard tile drill-in
        await searched.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("set:OP01", vm.CollectionSearchQuery);
        _presets.Verify(p => p.SetActiveSortPreset(CardGame.OnePiece, null), Times.Never);
        _presets.Verify(p => p.SetActiveFilterPreset(CardGame.OnePiece, null), Times.Never);
    }

    [Fact]
    public void SetGame_ToAllGames_DoesNotWipePreviousGamesActivePresets()
    {
        var sortPreset = new SortPreset { Name = "MySort", Game = CardGame.OnePiece, SortLevels = [] };
        var filterPreset = new FilterPreset { Name = "MyFilter", Game = CardGame.OnePiece };
        _presets.Setup(p => p.GetSortPresets(CardGame.OnePiece)).Returns([sortPreset]);
        _presets.Setup(p => p.GetFilterPresets(CardGame.OnePiece)).Returns([filterPreset]);
        _presets.Setup(p => p.GetActiveSortPreset(CardGame.OnePiece)).Returns(sortPreset);
        _presets.Setup(p => p.GetActiveFilterPreset(CardGame.OnePiece)).Returns(filterPreset);

        var vm = CreateVm();
        vm.SetGame(CardGame.OnePiece);   // concrete game with active sort + filter presets
        _presets.Invocations.Clear();    // ignore the persistence from loading the concrete game

        vm.SetGame(null);                // switch to All Games — must not persist a null over the saved presets

        _presets.Verify(p => p.SetActiveSortPreset(It.IsAny<CardGame>(), null), Times.Never);
        _presets.Verify(p => p.SetActiveFilterPreset(It.IsAny<CardGame>(), null), Times.Never);
    }

    [Fact]
    public void LoadTagFlyoutItems_NoSelection_ProducesEmptyList()
    {
        var vm = CreateVm();
        vm.GetSelectedCards = () => [];

        vm.LoadTagFlyoutItems();

        Assert.Empty(vm.TagFlyoutItems);
    }

    [Fact]
    public void LoadTagFlyoutItems_ComputesTriStateAcrossSelection()
    {
        // CreateVm() registers a catch-all GetTagsByLots(It.IsAny<...>()) default; Moq resolves
        // overlapping setups by "most recently registered wins", so the specific setup below must
        // be registered after CreateVm() to take precedence — same convention as the SetGame_*
        // tests above, which configure CreateVm() first and override specific behavior after.
        var vm = CreateVm();

        _tags.Setup(t => t.GetAllTags()).Returns([
            new TagSummary { Id = 1, Name = "Foil", UsageCount = 2 },
            new TagSummary { Id = 2, Name = "PSA", UsageCount = 1 },
            new TagSummary { Id = 3, Name = "Unused", UsageCount = 0 },
        ]);
        _tags.Setup(t => t.GetTagsByLots(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 10, 20 }))))
             .Returns(new Dictionary<int, List<string>>
             {
                 [10] = ["Foil", "PSA"],
                 [20] = ["Foil"],
             });

        vm.GetSelectedCards = () => [new CollectionCard { Id = 10 }, new CollectionCard { Id = 20 }];

        vm.LoadTagFlyoutItems();

        Assert.Equal(TagCheckState.Checked, vm.TagFlyoutItems.Single(t => t.Name == "Foil").State);
        Assert.Equal(TagCheckState.Indeterminate, vm.TagFlyoutItems.Single(t => t.Name == "PSA").State);
        Assert.Equal(TagCheckState.Unchecked, vm.TagFlyoutItems.Single(t => t.Name == "Unused").State);
    }

    [Fact]
    public void ToggleTagFlyoutItem_Apply_WritesTagAndUpdatesDisplayedCard()
    {
        var vm = CreateVm();
        var card = new CollectionCard { Id = 10 };
        vm.GetSelectedCards = () => [card];

        vm.ToggleTagFlyoutItemCommand.Execute(("Foil", true));

        _tags.Verify(t => t.AddTagToLots(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 10 })), "Foil"), Times.Once);
        Assert.Contains("Foil", card.Tags);
    }

    [Fact]
    public void ToggleTagFlyoutItem_Remove_WritesRemovalAndUpdatesDisplayedCard()
    {
        var vm = CreateVm();
        var card = new CollectionCard { Id = 10, Tags = ["Foil"] };
        vm.GetSelectedCards = () => [card];

        vm.ToggleTagFlyoutItemCommand.Execute(("Foil", false));

        _tags.Verify(t => t.RemoveTagFromLots(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 10 })), "Foil"), Times.Once);
        Assert.DoesNotContain("Foil", card.Tags);
    }

    [Fact]
    public void ToggleTagFlyoutItem_ExpandsStackedIds()
    {
        var vm = CreateVm();
        var card = new CollectionCard { Id = 10, StackedIds = [10, 11, 12] };
        vm.GetSelectedCards = () => [card];

        vm.ToggleTagFlyoutItemCommand.Execute(("Foil", true));

        _tags.Verify(t => t.AddTagToLots(It.Is<IEnumerable<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { 10, 11, 12 })), "Foil"), Times.Once);
    }

    [Fact]
    public void CreateTagFlyoutItem_TrimsAndAppliesAsNewChecked()
    {
        var vm = CreateVm();
        var card = new CollectionCard { Id = 10 };
        vm.GetSelectedCards = () => [card];

        vm.CreateTagFlyoutItemCommand.Execute("  Brand New  ");

        _tags.Verify(t => t.AddTagToLots(It.IsAny<IEnumerable<int>>(), "Brand New"), Times.Once);
        Assert.Contains("Brand New", card.Tags);
        Assert.Equal(TagCheckState.Checked, vm.TagFlyoutItems.Single(t => t.Name == "Brand New").State);
    }

    [Fact]
    public void CreateTagFlyoutItem_BlankName_IsNoOp()
    {
        var vm = CreateVm();
        vm.GetSelectedCards = () => [new CollectionCard { Id = 10 }];

        vm.CreateTagFlyoutItemCommand.Execute("   ");

        _tags.Verify(t => t.AddTagToLots(It.IsAny<IEnumerable<int>>(), It.IsAny<string>()), Times.Never);
    }

    // Integration test: exercises the toggle -> DB write -> re-query round trip through a REAL
    // TagService (backed by SQLite), not the Mock<ITagService> used by every other test in this
    // class. This deliberately bypasses CreateVm() (which always wires the mock) and constructs
    // CollectionViewModel directly, following the in-memory SQLite pattern from
    // OmniCard.Tests/Services/TagServiceTests.cs.
    [Fact]
    public void ToggleTagFlyoutItem_ThroughRealTagService_PersistsAndReloadReflectsWrite()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(connection)
            .Options;
        using (var ctx = new OmniCardDbContext(options))
            ctx.Database.EnsureCreated();

        var realTagService = new TagService(new MockFactory(options));

        _presets.Setup(p => p.GetSortPresets(It.IsAny<CardGame>())).Returns([]);
        _presets.Setup(p => p.GetFilterPresets(It.IsAny<CardGame>())).Returns([]);
        _containers.Setup(c => c.GetAll()).Returns([]);
        _query.Setup(q => q.GetLocationOverviewsAsync(It.IsAny<CardGame?>())).ReturnsAsync([]);
        _card.Setup(c => c.GetSearchCount(It.IsAny<string>(), It.IsAny<CardGame?>(), It.IsAny<int?>(),
                                          It.IsAny<FilterPreset?>(), It.IsAny<bool>()))
             .Returns(0);
        _listing.Setup(l => l.GetActiveListingStatusByLot(It.IsAny<IEnumerable<int>>()))
                .Returns(new Dictionary<int, ListingStatus>());

        var vm = new CollectionViewModel(
            _card.Object,
            _containers.Object,
            _presets.Object,
            _dialog.Object,
            _query.Object,
            Options.Create(new DisplaySettings()),
            _dataPath.Object,
            NullLogger<CollectionViewModel>.Instance,
            _ebayListing.Object,
            Options.Create(new EbaySettings()),
            _listing.Object,
            realTagService);

        var card = new CollectionCard { Id = 10 };
        vm.GetSelectedCards = () => [card];

        vm.ToggleTagFlyoutItemCommand.Execute(("Foil", true));

        // Reload from a fresh LoadTagFlyoutItems() call, which re-queries the real TagService/DB.
        vm.LoadTagFlyoutItems();

        Assert.Equal(TagCheckState.Checked, vm.TagFlyoutItems.Single(t => t.Name == "Foil").State);
        Assert.Contains("Foil", realTagService.GetTagsForLot(10));
    }

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
