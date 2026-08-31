using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Collection;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class ScanSessionServiceTests : IDisposable
{
    private readonly string _root;
    private readonly TestPaths _paths;
    private readonly ScanSessionService _service;

    public ScanSessionServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ocss-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = new TestPaths(_root);

        var storage = new Mock<IStorageContainerService>();
        storage.Setup(s => s.GetAll()).Returns([]);

        _service = new ScanSessionService(
            _paths, storage.Object, [], NullLogger<ScanSessionService>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private ScannedCard MakeCard(string imageContent = "fake-image-bytes")
    {
        Directory.CreateDirectory(_paths.TempScansDirectory);
        var img = Path.Combine(_paths.TempScansDirectory, Guid.NewGuid() + ".png");
        File.WriteAllText(img, imageContent);

        var card = new ScannedCard
        {
            TempImagePath = img,
            Hash = 0xABCDEF1234567890,
            ArtHashes = [1UL, 2UL],
            Game = CardGame.Mtg,
            Condition = "LP",
            IsFoil = true,
            FoilType = "Etched",
            PurchasePrice = 3.50m,
            FlagReason = FlagReason.VeryLowConfidence,
            Match = new CardMatch
            {
                Name = "Fumigate",
                SetCode = "MKC",
                SetName = "Murders at Karlov Manor Commander",
                CollectorNumber = "66",
                Rarity = "R",
                GameSpecificId = "abc-123",
                Confidence = 100,
            },
        };
        card.Tags.Add("keep");
        card.Tags.Add("binder");
        return card;
    }

    [Fact]
    public async Task SaveThenOpen_RoundTripsCardsAndImages()
    {
        var card = MakeCard();
        var session = new ScanSession { Name = "MyBatch" };
        var file = Path.Combine(_root, "batch.ocss");

        await _service.SaveAsync(session, [card], file);
        Assert.True(File.Exists(file));

        var result = await _service.OpenAsync(file);

        Assert.Equal("MyBatch", result.Session.Name);
        Assert.Equal(file, result.Session.FilePath);
        Assert.False(result.Session.HasUnsavedChanges);

        var opened = Assert.Single(result.Cards);
        Assert.Equal(0xABCDEF1234567890, opened.Hash);
        Assert.Equal([1UL, 2UL], opened.ArtHashes!);
        Assert.Equal("LP", opened.Condition);
        Assert.True(opened.IsFoil);
        Assert.Equal("Etched", opened.FoilType);
        Assert.Equal(3.50m, opened.PurchasePrice);
        Assert.Equal(FlagReason.VeryLowConfidence, opened.FlagReason);
        Assert.Equal(new[] { "keep", "binder" }, opened.Tags);

        Assert.NotNull(opened.Match);
        Assert.Equal("Fumigate", opened.Match!.Name);
        Assert.Equal("MKC", opened.Match.SetCode);
        Assert.Equal("66", opened.Match.CollectorNumber);

        // Image was extracted to a real temp file with the original bytes.
        Assert.True(File.Exists(opened.TempImagePath));
        Assert.Equal("fake-image-bytes", File.ReadAllText(opened.TempImagePath));
    }

    [Fact]
    public async Task Open_RehydratesMatchSourceFromGameService()
    {
        // A game service that resolves the id back to a catalog object (as the real ones do), so the
        // reopened match's Source is repopulated for commit-time attribute extraction.
        var sourceObj = new object();
        var game = new Mock<ICardGameService>();
        game.SetupGet(g => g.Game).Returns(CardGame.Mtg);
        game.Setup(g => g.FindCardById("abc-123")).Returns(sourceObj);

        var service = new ScanSessionService(_paths, Mock.Of<IStorageContainerService>(), [game.Object],
            NullLogger<ScanSessionService>.Instance);

        var file = Path.Combine(_root, "src.ocss");
        await service.SaveAsync(new ScanSession(), [MakeCard()], file);

        var result = await service.OpenAsync(file);

        Assert.Same(sourceObj, Assert.Single(result.Cards).Match!.Source);
    }

    [Fact]
    public async Task Autosave_ThenRecover_RoundTrips()
    {
        Assert.False(_service.TryGetRecoverable(out _));

        var session = new ScanSession { Name = "Crash" };
        await _service.AutosaveAsync(session, [MakeCard()]);

        Assert.True(_service.TryGetRecoverable(out var savedUtc));
        Assert.True(savedUtc <= DateTime.UtcNow);

        var recovered = await _service.RecoverAsync();
        Assert.Single(recovered.Cards);
        // A recovered session isn't tied to a user-visible file yet.
        Assert.Null(recovered.Session.FilePath);

        _service.ClearRecovery();
        Assert.False(_service.TryGetRecoverable(out _));
    }

    [Fact]
    public async Task Save_WithMissingImageFile_StillRoundTripsMetadata()
    {
        var card = MakeCard();
        File.Delete(card.TempImagePath); // simulate a temp image that was cleaned up

        var file = Path.Combine(_root, "noimg.ocss");
        await _service.SaveAsync(new ScanSession(), [card], file);

        var opened = Assert.Single((await _service.OpenAsync(file)).Cards);
        Assert.Equal("", opened.TempImagePath);        // no image, but the card survives
        Assert.Equal("Fumigate", opened.Match!.Name);
    }

    private sealed class TestPaths(string dataDir) : IDataPathService
    {
        public string DataDirectory => dataDir;
        public string ScansDirectory => Path.Combine(dataDir, "scans");
        public string TempScansDirectory => Path.Combine(dataDir, "temp_scans");
        public string SymbolsCacheDirectory => Path.Combine(dataDir, "symbols");
        public string LogsDirectory => Path.Combine(dataDir, "logs");
        public string TradesDirectory => Path.Combine(dataDir, "trades");
        // SessionsDirectory + ScanSessionRecoveryPath use the interface defaults (derived from DataDirectory).
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
