using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Web.Services;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Settings;
using OmniCard.Shared.Storage;
using OmniCard.Collection.Inventory;

namespace OmniCard.Tests.Web;

/// <summary>
/// Covers the writable binder-edit service used by the web companion. Same in-memory SQLite pattern
/// as the desktop service tests (keep the connection open for the fixture's lifetime).
/// </summary>
public class WebBinderCardServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly IDbContextFactory<OmniCardDbContext> _factory;
    private readonly WebBinderCardService _service;
    private readonly StorageContainerService _containers;
    private readonly int _binderId;

    public WebBinderCardServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();

        _factory = new MockFactory(_opts);
        _service = new WebBinderCardService(_factory, new StubDataPath());
        _containers = new StorageContainerService(_factory);
        _binderId = _containers.Create("Binder A", ContainerType.Binder).Id;
    }

    public void Dispose() => _conn.Dispose();

    private int AddLot(string name, int? page = null, int? slot = null, bool foil = false, string condition = "NM")
    {
        using var ctx = new OmniCardDbContext(_opts);
        var product = new Product
        {
            Game = CardGame.Pokemon,
            Category = ProductCategory.Single,
            GameCardId = name.Replace(" ", "").ToLowerInvariant(),
            Foil = foil,
            Name = name,
            SetCode = "SET",
            SetName = "Set Name",
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        var lot = new InventoryLot
        {
            ProductId = product.Id,
            LocationId = _binderId,
            Page = page,
            Slot = slot,
            Condition = condition,
        };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    [Fact]
    public void GetUnplacedBinderCards_ReturnsOnlyUnplacedInContainer()
    {
        AddLot("Placed Card", page: 1, slot: 0);
        var unplacedId = AddLot("Unplaced Card");

        var result = _service.GetUnplacedBinderCards(_binderId, null);

        Assert.Single(result);
        Assert.Equal(unplacedId, result[0].Id);
    }

    [Fact]
    public void GetUnplacedBinderCards_AppliesScryfallFilter()
    {
        AddLot("Charizard");
        AddLot("Blastoise");

        var result = _service.GetUnplacedBinderCards(_binderId, new FilterPreset { Query = "name:char" });

        Assert.Single(result);
        Assert.Equal("Charizard", result[0].Name);
    }

    [Fact]
    public void MoveCardsToContainer_ClearsPlacementAndMoves()
    {
        var lotId = AddLot("Mover", page: 1, slot: 2);
        var targetId = _containers.Create("Box B", ContainerType.Box).Id;

        _service.MoveCardsToContainer([lotId], targetId);

        using var ctx = new OmniCardDbContext(_opts);
        var lot = ctx.Lots.Single(l => l.Id == lotId);
        Assert.Equal(targetId, lot.LocationId);
        Assert.Null(lot.Page);
        Assert.Null(lot.Slot);
    }

    [Fact]
    public void MoveCardsToContainer_IntoMismatchedGameDeckBox_Throws()
    {
        var lotId = AddLot("Pikachu"); // AddLot creates Pokémon products
        // A deck box locked to Magic must reject the Pokémon card.
        var deckBox = _containers.Create("MTG Deck", ContainerType.DeckBox, game: CardGame.Mtg);

        var ex = Assert.Throws<DeckBoxGameMismatchException>(() =>
            _service.MoveCardsToContainer([lotId], deckBox.Id));
        Assert.Equal(CardGame.Mtg, ex.DeckBoxGame);
        Assert.Equal(CardGame.Pokemon, ex.OffendingGame);

        // The lot stays put — nothing was moved.
        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal(_binderId, ctx.Lots.Single(l => l.Id == lotId).LocationId);
    }

    [Fact]
    public void MoveCardsToContainer_IntoMatchingGameDeckBox_Succeeds()
    {
        var lotId = AddLot("Charizard");
        var deckBox = _containers.Create("Pokémon Deck", ContainerType.DeckBox, game: CardGame.Pokemon);

        _service.MoveCardsToContainer([lotId], deckBox.Id);

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal(deckBox.Id, ctx.Lots.Single(l => l.Id == lotId).LocationId);
    }

    [Fact]
    public void SetFoil_MovesLotToFoilProduct()
    {
        var lotId = AddLot("Foiler", foil: false);

        _service.SetFoil([lotId], true);

        using var ctx = new OmniCardDbContext(_opts);
        var lot = ctx.Lots.Include(l => l.Product).Single(l => l.Id == lotId);
        Assert.True(lot.Product.Foil);
    }

    [Fact]
    public void SetCondition_UpdatesLot()
    {
        var lotId = AddLot("Condish", condition: "NM");

        _service.SetCondition([lotId], "LP");

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal("LP", ctx.Lots.Single(l => l.Id == lotId).Condition);
    }

    [Fact]
    public void SetQuantity_Bulk_UpdatesEveryLot()
    {
        var a = AddLot("Bolt A");
        var b = AddLot("Bolt B");

        _service.SetQuantity([a, b], 4);

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal(4, ctx.Lots.Single(l => l.Id == a).Quantity);
        Assert.Equal(4, ctx.Lots.Single(l => l.Id == b).Quantity);
    }

    [Fact]
    public void SetQuantity_Bulk_FloorsAtOne()
    {
        var lotId = AddLot("Clamp");

        _service.SetQuantity([lotId], 0);

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal(1, ctx.Lots.Single(l => l.Id == lotId).Quantity);
    }

    [Fact]
    public void BulkUpdateField_SetsNoteAndPriceOnEveryLot()
    {
        var a = AddLot("Note A");
        var b = AddLot("Note B");

        _service.BulkUpdateField([a, b], c =>
        {
            c.Note = "picked for trade";
            c.PurchasePrice = 2.50m;
        });

        using var ctx = new OmniCardDbContext(_opts);
        foreach (var id in new[] { a, b })
        {
            var lot = ctx.Lots.Single(l => l.Id == id);
            Assert.Equal("picked for trade", lot.Note);
            Assert.Equal(2.50m, lot.UnitCost);
        }
    }

    [Fact]
    public void GetCollectionCards_ReturnsSelectedLotsAsCollectionCards()
    {
        var a = AddLot("Alpha");
        var b = AddLot("Bravo");
        AddLot("Charlie"); // not selected

        var result = _service.GetCollectionCards([a, b]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Id == a && c.Name == "Alpha");
        Assert.Contains(result, c => c.Id == b && c.Name == "Bravo");
        Assert.DoesNotContain(result, c => c.Name == "Charlie");
    }

    [Fact]
    public void GetCollectionCards_ExcludesNonSingleProductLots()
    {
        var single = AddLot("A Single");
        int sealedLotId;
        using (var ctx = new OmniCardDbContext(_opts))
        {
            var sealedProduct = new Product
            {
                Game = CardGame.Pokemon,
                Category = ProductCategory.Box, // sealed booster box, not a single card
                GameCardId = "booster-box",
                Name = "Booster Box",
                SetCode = "SET",
                SetName = "Set Name",
            };
            ctx.Products.Add(sealedProduct);
            ctx.SaveChanges();
            var lot = new InventoryLot { ProductId = sealedProduct.Id, LocationId = _binderId, Condition = "NM" };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();
            sealedLotId = lot.Id;
        }

        var result = _service.GetCollectionCards([single, sealedLotId]);

        Assert.Single(result);
        Assert.Equal(single, result[0].Id);
    }

    [Fact]
    public void DeleteCollectionCard_RemovesLot()
    {
        var lotId = AddLot("Doomed");

        _service.DeleteCollectionCard(lotId);

        using var ctx = new OmniCardDbContext(_opts);
        Assert.False(ctx.Lots.Any(l => l.Id == lotId));
    }

    [Fact]
    public void AddMissingCardToSlot_PlacesCardAndSwapsOccupant()
    {
        var occupantId = AddLot("Occupant", page: 1, slot: 0);
        var match = new CardMatch
        {
            GameSpecificId = "newcard",
            Name = "New Card",
            SetCode = "SET",
            SetName = "Set Name",
            CollectorNumber = "42",
            Rarity = "Rare",
        };

        _service.AddMissingCardToSlot(match, CardGame.Pokemon, "NM", false, null, null, _binderId, 1, 0);

        using var ctx = new OmniCardDbContext(_opts);
        // Occupant displaced back to the unplaced pool.
        var occupant = ctx.Lots.Single(l => l.Id == occupantId);
        Assert.Null(occupant.Page);
        Assert.Null(occupant.Slot);
        // New card now occupies page 1, slot 0.
        var placed = ctx.Lots.Include(l => l.Product)
            .Single(l => l.LocationId == _binderId && l.Page == 1 && l.Slot == 0);
        Assert.Equal("New Card", placed.Product.Name);
    }

    private int AddLotIn(int containerId, string name, int quantity = 1, string condition = "NM")
    {
        using var ctx = new OmniCardDbContext(_opts);
        var product = new Product
        {
            Game = CardGame.Pokemon,
            Category = ProductCategory.Single,
            GameCardId = name.Replace(" ", "").ToLowerInvariant(),
            Name = name,
            SetCode = "SET",
            SetName = "Set Name",
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        var lot = new InventoryLot
        {
            ProductId = product.Id,
            LocationId = containerId,
            Quantity = quantity,
            Condition = condition,
        };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    [Fact]
    public void PlaceOwnedCardInSlot_SingleCopy_MovesWholeLotIntoPocket()
    {
        var boxId = _containers.Create("Box B", ContainerType.Box).Id;
        var lotId = AddLotIn(boxId, "Solo", quantity: 1);

        _service.PlaceOwnedCardInSlot(lotId, _binderId, 2, 3);

        using var ctx = new OmniCardDbContext(_opts);
        var lot = ctx.Lots.Single(l => l.Id == lotId);
        Assert.Equal(_binderId, lot.LocationId);
        Assert.Equal(2, lot.Page);
        Assert.Equal(3, lot.Slot);
        // No extra lot was created for a whole-lot move.
        Assert.Equal(1, ctx.Lots.Count(l => l.Product.Name == "Solo"));
    }

    [Fact]
    public void PlaceOwnedCardInSlot_MultiCopy_SplitsOneCopyLeavingRemainder()
    {
        var boxId = _containers.Create("Box B", ContainerType.Box).Id;
        var lotId = AddLotIn(boxId, "Stack", quantity: 3);

        _service.PlaceOwnedCardInSlot(lotId, _binderId, 1, 0);

        using var ctx = new OmniCardDbContext(_opts);
        // Original lot keeps the remainder where it was.
        var source = ctx.Lots.Single(l => l.Id == lotId);
        Assert.Equal(2, source.Quantity);
        Assert.Equal(boxId, source.LocationId);
        Assert.Null(source.Page);
        // A new single-copy lot sits in the pocket.
        var placed = ctx.Lots.Include(l => l.Product)
            .Single(l => l.LocationId == _binderId && l.Page == 1 && l.Slot == 0);
        Assert.Equal("Stack", placed.Product.Name);
        Assert.Equal(1, placed.Quantity);
        Assert.NotEqual(lotId, placed.Id);
    }

    [Fact]
    public void PlaceOwnedCardInSlot_DisplacesExistingOccupant()
    {
        var occupantId = AddLot("Occupant", page: 1, slot: 0);
        var boxId = _containers.Create("Box B", ContainerType.Box).Id;
        var incomingId = AddLotIn(boxId, "Incoming", quantity: 1);

        _service.PlaceOwnedCardInSlot(incomingId, _binderId, 1, 0);

        using var ctx = new OmniCardDbContext(_opts);
        var occupant = ctx.Lots.Single(l => l.Id == occupantId);
        Assert.Null(occupant.Page);
        Assert.Null(occupant.Slot);
        var incoming = ctx.Lots.Single(l => l.Id == incomingId);
        Assert.Equal(1, incoming.Page);
        Assert.Equal(0, incoming.Slot);
    }

    [Fact]
    public void PlaceOwnedCardInSlot_IntoMismatchedGameDeckBox_Throws()
    {
        var boxId = _containers.Create("Box B", ContainerType.Box).Id;
        var lotId = AddLotIn(boxId, "Pikachu"); // Pokémon
        var deckBox = _containers.Create("MTG Deck", ContainerType.DeckBox, game: CardGame.Mtg);

        Assert.Throws<DeckBoxGameMismatchException>(() =>
            _service.PlaceOwnedCardInSlot(lotId, deckBox.Id, 1, 0));

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal(boxId, ctx.Lots.Single(l => l.Id == lotId).LocationId);
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
