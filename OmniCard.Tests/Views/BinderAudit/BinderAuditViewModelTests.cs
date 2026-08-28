using System.Collections.ObjectModel;
using System.IO;
using OmniCard.Collection;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.BinderAudit;

namespace OmniCard.Tests.Views.BinderAudit;

public class BinderAuditViewModelTests
{
    private const int ContainerId = 1;

    // --- Loading & rendering ---------------------------------------------------------------------

    [Fact]
    public void Load_FillsSpreadFromPlacedCards()
    {
        var containers = new FakeContainerService(slotsPerPage: 9, columns: 3, totalPages: 2);
        containers.Place(page: 1, slot: 0, Card(10));
        var vm = CreateVm(containers, out _);

        vm.Load(ContainerId);

        // Spread 0 shows page 1 on the right, nothing on the left.
        Assert.False(vm.HasLeftPage);
        Assert.True(vm.HasRightPage);
        Assert.Equal(9, vm.RightPageSlots.Count);
        Assert.True(vm.RightPageSlots[0].IsOccupied);
        Assert.False(vm.RightPageSlots[1].IsOccupied);
        Assert.Contains("of 1", vm.AuditProgress); // one placed card in the whole binder
    }

    // --- Marking gates ---------------------------------------------------------------------------

    [Fact]
    public void MarkCommands_OnlyApplyToTheRightPocketKind()
    {
        var containers = new FakeContainerService(slotsPerPage: 9, columns: 3, totalPages: 2);
        containers.Place(page: 1, slot: 0, Card(10));
        var vm = CreateVm(containers, out _);
        vm.Load(ContainerId);

        var filled = vm.RightPageSlots[0];
        var empty = vm.RightPageSlots[1];

        // Extra is empty-only; Correct/Missing/Wrong are filled-only.
        vm.MarkExtraCommand.Execute(filled);
        Assert.Equal(AuditMark.None, filled.Mark);
        vm.MarkCorrectCommand.Execute(empty);
        Assert.Equal(AuditMark.None, empty.Mark);

        vm.MarkCorrectCommand.Execute(filled);
        Assert.Equal(AuditMark.Correct, filled.Mark);
        vm.MarkExtraCommand.Execute(empty);
        Assert.Equal(AuditMark.ExtraPresent, empty.Mark);
    }

    [Fact]
    public void MarkCommand_TogglesOffWhenRepeated()
    {
        var containers = new FakeContainerService(slotsPerPage: 9, columns: 3, totalPages: 2);
        containers.Place(page: 1, slot: 0, Card(10));
        var vm = CreateVm(containers, out _);
        vm.Load(ContainerId);

        var filled = vm.RightPageSlots[0];
        vm.MarkMissingCommand.Execute(filled);
        Assert.Equal(AuditMark.Missing, filled.Mark);
        vm.MarkMissingCommand.Execute(filled);
        Assert.Equal(AuditMark.None, filled.Mark);
    }

    [Fact]
    public void Marks_SurviveSpreadNavigation()
    {
        var containers = new FakeContainerService(slotsPerPage: 9, columns: 3, totalPages: 3);
        containers.Place(page: 1, slot: 0, Card(10));
        var vm = CreateVm(containers, out _);
        vm.Load(ContainerId);

        vm.MarkWrongCommand.Execute(vm.RightPageSlots[0]);
        Assert.Equal(AuditMark.Wrong, vm.RightPageSlots[0].Mark);

        vm.NextSpreadCommand.Execute(null); // spread 1 (pages 2-3)
        vm.FirstSpreadCommand.Execute(null); // back to spread 0 (page 1) — slots rebuilt

        Assert.Equal(AuditMark.Wrong, vm.RightPageSlots[0].Mark);
    }

    // --- Review & apply --------------------------------------------------------------------------

    [Fact]
    public void ApplyCorrections_BlockedUntilWrongAndExtraRowsHaveSelections()
    {
        var containers = new FakeContainerService(slotsPerPage: 9, columns: 3, totalPages: 2);
        containers.Place(page: 1, slot: 0, Card(10));
        var vm = CreateVm(containers, out _);
        vm.Load(ContainerId);

        vm.MarkWrongCommand.Execute(vm.RightPageSlots[0]);
        vm.BeginReviewCommand.Execute(null);

        Assert.True(vm.IsReviewMode);
        Assert.Single(vm.ReviewRows);
        Assert.False(vm.ApplyCorrectionsCommand.CanExecute(null));

        vm.ReviewRows[0].SelectedMatch = Match("new-id", "Correct Card");
        Assert.True(vm.ApplyCorrectionsCommand.CanExecute(null));
    }

    [Fact]
    public void ApplyCorrections_MapsEachMarkToTheRightServiceCall()
    {
        var containers = new FakeContainerService(slotsPerPage: 9, columns: 3, totalPages: 2);
        var missingCard = Card(10, page: 1, slot: 0);
        var wrongCard = Card(11, page: 1, slot: 1);
        containers.Place(page: 1, slot: 0, missingCard);
        containers.Place(page: 1, slot: 1, wrongCard);
        var vm = CreateVm(containers, out var cards);
        vm.Load(ContainerId);

        vm.MarkMissingCommand.Execute(vm.RightPageSlots[0]);
        vm.MarkWrongCommand.Execute(vm.RightPageSlots[1]);
        vm.MarkExtraCommand.Execute(vm.RightPageSlots[2]); // empty pocket, slot 2

        vm.BeginReviewCommand.Execute(null);

        var wrongRow = vm.ReviewRows.Single(r => r.Mark == AuditMark.Wrong);
        wrongRow.SelectedMatch = Match("correct-id", "Correct Card");
        var extraRow = vm.ReviewRows.Single(r => r.Mark == AuditMark.ExtraPresent);
        extraRow.SelectedMatch = Match("extra-id", "Extra Card");

        var closed = false;
        vm.RequestClose = () => closed = true;

        vm.ApplyCorrectionsCommand.Execute(null);

        // Missing -> flag the existing lot.
        Assert.Equal(missingCard.Id, Assert.Single(cards.MissingFlagged));

        // Wrong -> reassign the existing card's identity, keep its slot.
        var updated = Assert.Single(cards.Updated);
        Assert.Equal(wrongCard.Id, updated.Id);
        Assert.Equal("correct-id", updated.GameCardId);
        Assert.Equal("Correct Card", updated.Name);
        Assert.Equal(1, updated.Page);
        Assert.Equal(1, updated.Slot);

        // Extra -> add a new card into the empty pocket.
        var added = Assert.Single(cards.Added);
        Assert.Equal("extra-id", added.Match.GameSpecificId);
        Assert.Equal(ContainerId, added.ContainerId);
        Assert.Equal(1, added.Page);
        Assert.Equal(2, added.Slot);

        Assert.True(closed);
    }

    // --- Helpers ---------------------------------------------------------------------------------

    private static BinderAuditViewModel CreateVm(FakeContainerService containers, out RecordingCardService cards)
    {
        cards = new RecordingCardService();
        return new BinderAuditViewModel(containers, cards, new OmniCard.Data.DataPathService(Path.GetTempPath()));
    }

    // ImageUri is set so the VM never needs a game service to hydrate art.
    private static CollectionCard Card(int id, int page = 1, int slot = 0) => new()
    {
        Id = id,
        Game = CardGame.Mtg,
        GameCardId = $"card-{id}",
        Name = $"Card {id}",
        SetCode = "lea",
        Number = "1",
        ImageUri = "https://img/card.jpg",
        ContainerId = ContainerId,
        Page = page,
        Slot = slot,
    };

    private static CardMatch Match(string id, string name) => new()
    {
        Name = name,
        SetCode = "leb",
        SetName = "Beta",
        CollectorNumber = "2",
        Rarity = "rare",
        GameSpecificId = id,
        ImageUri = "https://img/new.jpg",
    };

    // --- Fakes -----------------------------------------------------------------------------------

    private sealed class FakeContainerService(int slotsPerPage, int columns, int totalPages) : IStorageContainerService
    {
        private readonly Dictionary<int, List<CollectionCard>> _byPage = [];

        public void Place(int page, int slot, CollectionCard card)
        {
            card.Page = page;
            card.Slot = slot;
            if (!_byPage.TryGetValue(page, out var list))
                _byPage[page] = list = [];
            list.Add(card);
        }

        public List<StorageContainer> GetAll() =>
            [new StorageContainer { Id = ContainerId, Name = "Test Binder", ContainerType = ContainerType.Binder }];

        public BinderLayout GetBinderLayout(int containerId) => new()
        {
            SlotsPerPage = slotsPerPage,
            Columns = columns,
            TotalPages = totalPages,
            SheetSides = Enumerable.Repeat(1, totalPages).ToList(),
        };

        public List<CollectionCard> GetPlacedCardsOnPage(int containerId, int page) =>
            _byPage.TryGetValue(page, out var list) ? [.. list] : [];

        // Unused by the audit VM.
        public StorageContainer GetBulk() => throw new NotImplementedException();
        public StorageContainer Create(string name, ContainerType type, int slotsPerPage = 9) => throw new NotImplementedException();
        public void Rename(int id, string newName) => throw new NotImplementedException();
        public void Delete(int id, bool moveCardsToBulk = true) => throw new NotImplementedException();
        public int GetCardCount(int containerId) => throw new NotImplementedException();
        public void SetCoverCard(int containerId, int? cardId) => throw new NotImplementedException();
        public List<CollectionCard> GetCardsInContainer(int containerId) => throw new NotImplementedException();
        public void SetExcludeFromDeckCheck(int containerId, bool exclude) => throw new NotImplementedException();
        public void SetAlwaysAvailable(int containerId, bool alwaysAvailable) => throw new NotImplementedException();
        public void AddBinderSheet(int containerId, bool doubleSided) => throw new NotImplementedException();
        public BinderSheetInfo GetSheetForPage(int containerId, int page) => throw new NotImplementedException();
        public List<BinderSheetInfo> GetSheets(int containerId) => throw new NotImplementedException();
        public void InsertBinderSheet(int containerId, int insertIndex, bool doubleSided) => throw new NotImplementedException();
        public void MoveBinderSheet(int containerId, int fromPage, int toIndex) => throw new NotImplementedException();
        public void RemoveBinderSheet(int containerId, int page) => throw new NotImplementedException();
        public void ShiftPage(int containerId, int page, int deltaPages, BinderShiftScope scope) => throw new NotImplementedException();
        public void SetSlotsPerPage(int containerId, int slotsPerPage) => throw new NotImplementedException();
        public void SetColumns(int containerId, int columns) => throw new NotImplementedException();
        public void AssignCardToSlot(int lotId, int containerId, int page, int slot) => throw new NotImplementedException();
        public void UnassignFromPage(int lotId) => throw new NotImplementedException();
    }

    public sealed record AddedCard(CardMatch Match, int ContainerId, int Page, int Slot);

    private sealed class RecordingCardService : ICardService
    {
        public List<int> MissingFlagged { get; } = [];
        public List<CollectionCard> Updated { get; } = [];
        public List<AddedCard> Added { get; } = [];

        public void SetCardMissing(int lotId) => MissingFlagged.Add(lotId);
        public void UpdateCollectionCard(CollectionCard card) => Updated.Add(card);
        public void AddMissingCardToSlot(CardMatch match, CardGame game, string condition, bool isFoil, string? foilType, decimal? purchasePrice, int containerId, int page, int slot)
            => Added.Add(new AddedCard(match, containerId, page, slot));

        public CardGame SelectedGame { get => CardGame.Mtg; set { } }

        // Unused by the audit VM.
        public ObservableCollection<ScannedCard> ScannedCards => throw new NotImplementedException();
        public HashSet<string>? SelectedSetFilter { get => null; set { } }
        public bool DefaultIsFoil { get; set; }
        public string? DefaultFoilType { get; set; }
        public decimal? DefaultPurchasePrice { get; set; }
        public IReadOnlyList<CardGame> AvailableGames => throw new NotImplementedException();
        public ICardGameService ActiveGameService => throw new NotImplementedException();
        public Action<HashStageResult>? OnHashStage { get; set; }
        public ulong LastComputedHash => throw new NotImplementedException();
        public IOcrMatchingService OcrService => throw new NotImplementedException();
        public ICardGameService GetGameService(CardGame game) => throw new NotImplementedException();
        public void AddFromStream(Stream stream) => throw new NotImplementedException();
        public void ReprocessScans() => throw new NotImplementedException();
        public void CommitScans(IEnumerable<ScannedCard> scannedCards) => throw new NotImplementedException();
        public void CommitScans(IEnumerable<ScannedCard> scannedCards, StorageContainer? activeContainer, int? page, int? slot, string? section, IProgress<string>? progress = null) => throw new NotImplementedException();
        public void SearchCollection(string query, CardGame? gameFilter, ObservableCollection<CollectionCard> results) => throw new NotImplementedException();
        public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, ObservableCollection<CollectionCard> results) => throw new NotImplementedException();
        public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, ObservableCollection<CollectionCard> results) => throw new NotImplementedException();
        public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, bool stacked, ObservableCollection<CollectionCard> results) => throw new NotImplementedException();
        public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, bool stacked, int skip, int take, ObservableCollection<CollectionCard> results) => throw new NotImplementedException();
        public int GetSearchCount(string query, CardGame? gameFilter, int? containerFilter, FilterPreset? filterPreset, bool stacked) => throw new NotImplementedException();
        public HashSet<int> GetMatchingContainerIds(string query, CardGame? gameFilter) => throw new NotImplementedException();
        public List<CollectionCard> GetUnplacedBinderCards(int containerId, FilterPreset? filterPreset) => throw new NotImplementedException();
        public void MoveCardsToContainer(IEnumerable<int> cardIds, int containerId, string? section = null) => throw new NotImplementedException();
        public int MoveQuantityToContainer(int lotId, int quantity, int containerId, string? section = null) => throw new NotImplementedException();
        public void BulkUpdateField(IEnumerable<int> cardIds, Action<CollectionCard> update) => throw new NotImplementedException();
        public List<CollectionCard> GetCollectionCards(IEnumerable<int> cardIds) => throw new NotImplementedException();
        public void DeleteCollectionCard(int id) => throw new NotImplementedException();
        public Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame game, IProgress<string>? progress = null) => throw new NotImplementedException();
        public Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame? game, IProgress<string>? progress = null) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, decimal> GetCurrentPrices(CardGame game, IEnumerable<string> gameCardIds, bool foil) => throw new NotImplementedException();
        public List<string> GetDistinctFieldValues(string field, CardGame game) => throw new NotImplementedException();
        public List<MissingCard> GetMissingCardsForSet(CardGame game, string setCode) => throw new NotImplementedException();
        public void RemoveTempFile(ScannedCard card) => throw new NotImplementedException();
        public void ClearTempFiles() => throw new NotImplementedException();
        public void StartNewDiagnosticSession() => throw new NotImplementedException();
        public (int FlagResolutions, int MismatchLogs, int DiagnosticEvents) ClearDiagnosticLogs() => throw new NotImplementedException();
        public (int Deleted, int Errors) DeleteOrphanedScans(IProgress<string>? progress = null) => throw new NotImplementedException();
        public void AddCardToCollection(CardMatch match, CardGame game, string condition, bool isFoil, string? foilType, decimal? purchasePrice, int quantity, StorageContainer? container, int? page, int? slot, string? section) => throw new NotImplementedException();
        public bool IsFirstCopy(CardGame game, string gameCardId, bool isFoil) => throw new NotImplementedException();
        public void AnnotateScan(ScannedCard scan) => throw new NotImplementedException();
        public int ImportCollectionCards(IEnumerable<CollectionCard> cards, bool skipDuplicates) => throw new NotImplementedException();
        public ulong ComputeHashFromStream(Stream stream) => throw new NotImplementedException();
        public ulong ComputeEdgeHashFromStream(Stream stream) => throw new NotImplementedException();
        public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null) => throw new NotImplementedException();
    }
}
