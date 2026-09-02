using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Sets;

namespace OmniCard.Tests.Views.Sets;

public class SetsViewModelTests
{
    [Fact]
    public void Constructor_PopulatesGamesAndSets_ForFirstGame()
    {
        var vm = CreateVm(out _);

        Assert.Contains(CardGame.Mtg, vm.Games);
        Assert.Equal(CardGame.Mtg, vm.SelectedGame);
        Assert.Equal(2, vm.Sets.Count); // both seeded sets
    }

    [Fact]
    public void SetFilterText_NarrowsSets()
    {
        var vm = CreateVm(out _);

        vm.SetFilterText = "one";
        Assert.Single(vm.Sets);
        Assert.Equal("set1", vm.Sets[0].SetCode);

        vm.SetFilterText = "";
        Assert.Equal(2, vm.Sets.Count);
    }

    [Fact]
    public async Task LoadSet_PopulatesChecklistAndCompletion_AndEnablesExport()
    {
        var vm = CreateVm(out _);
        vm.SelectedSet = vm.Sets.Single(s => s.SetCode == "set1");

        Assert.False(vm.ExportWantListCommand.CanExecute(null));

        await vm.LoadSetCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.ChecklistCards.Count);
        Assert.Contains("owned", vm.CompletionText);
        Assert.NotNull(vm.Current);
        Assert.True(vm.ExportWantListCommand.CanExecute(null));
    }

    [Fact]
    public void ChangingGame_ClearsPreviousChecklist()
    {
        var vm = CreateVm(out _);
        vm.SelectedSet = vm.Sets.First();

        vm.SelectedGame = CardGame.Mtg; // re-select (change handler runs)
        Assert.Empty(vm.ChecklistCards);
        Assert.Null(vm.Current);
    }

    private static SetsViewModel CreateVm(out FakeChecklistService checklist)
    {
        checklist = new FakeChecklistService();
        var game = new FakeGameService(CardGame.Mtg,
        [
            new SetInfo("set1", "Set One"),
            new SetInfo("set2", "Set Two"),
        ]);

        var cardService = new Mock<ICardService>();
        cardService.SetupGet(c => c.AvailableGames).Returns([CardGame.Mtg]);

        return new SetsViewModel(
            checklist,
            new FakePdfExporter(),
            [game],
            cardService.Object,
            new DataPathService(System.IO.Path.GetTempPath()),
            NullLogger<SetsViewModel>.Instance);
    }

    private sealed class FakeChecklistService : ISetChecklistService
    {
        public Task<SetChecklist> BuildAsync(CardGame game, string setCode)
        {
            var cards = new List<SetChecklistCard>
            {
                new() { CollectorNumber = "1", Name = "One", OwnedQuantity = 1, Card = new CollectionCard() },
                new() { CollectorNumber = "2", Name = "Two", OwnedQuantity = 0, Card = new CollectionCard() },
            };
            return Task.FromResult(new SetChecklist
            {
                Game = game, SetCode = setCode, SetName = "Set One",
                Cards = cards, OwnedCount = 1, TotalCount = 2, OwnedPhysicalCount = 1,
            });
        }

        public SetChecklistReport BuildWantListReport(SetChecklist checklist) =>
            new() { Game = checklist.Game, SetCode = checklist.SetCode, SetName = checklist.SetName };
    }

    private sealed class FakePdfExporter : ISetChecklistPdfExporter
    {
        public void Export(SetChecklistReport report, string filePath) { }
    }

    private sealed class FakeGameService(CardGame game, List<SetInfo> sets) : ICardGameService
    {
        public CardGame Game => game;
        public MatchDiagnostics? LastMatchDiagnostics => null;
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) => null;
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => [];
        public List<CardMatch> GetPrintings(string cardName) => [];
        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => null;
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => [];
        public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
        public IReadOnlyList<SetInfo> GetAvailableSets() => sets;
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
        public List<SetCatalogCard> GetSetCards(string setCode) => [];
        public object? FindCardById(string gameCardId) => null;
    }
}
