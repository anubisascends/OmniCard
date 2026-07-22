using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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

    private CollectionViewModel CreateVm()
    {
        // Preset lookups are iterated, so they must return real (empty) lists, not null.
        _presets.Setup(p => p.GetSortPresets(It.IsAny<CardGame>())).Returns([]);
        _presets.Setup(p => p.GetFilterPresets(It.IsAny<CardGame>())).Returns([]);
        _containers.Setup(c => c.GetAll()).Returns([]);
        _query.Setup(q => q.GetLocationOverviewsAsync(It.IsAny<CardGame?>()))
              .ReturnsAsync([]);
        // Card-list search path with an empty result set: count 0, no rows added, empty status map.
        _card.Setup(c => c.GetSearchCount(It.IsAny<string>(), It.IsAny<CardGame>(), It.IsAny<int?>(),
                                          It.IsAny<FilterPreset?>(), It.IsAny<bool>()))
             .Returns(0);
        _listing.Setup(l => l.GetActiveListingStatusByLot(It.IsAny<IEnumerable<int>>()))
                .Returns(new Dictionary<int, ListingStatus>());

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
            _listing.Object);
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
        _card.Invocations.Clear();

        vm.SetGame(CardGame.OnePiece);
        await Task.Yield();           // let the fire-and-forget search run

        _card.Verify(c => c.SearchCollection(
            It.IsAny<string>(), CardGame.OnePiece, It.IsAny<int?>(),
            It.IsAny<SortPreset?>(), It.IsAny<FilterPreset?>(), It.IsAny<bool>(),
            0, It.IsAny<int>(), It.IsAny<ObservableCollection<CollectionCard>>()), Times.Once);
    }
}
