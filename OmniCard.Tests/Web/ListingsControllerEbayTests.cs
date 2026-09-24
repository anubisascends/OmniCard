using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Audit;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Ebay;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Storage;
using OmniCard.Shared.Games;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Scanning;
using OmniCard.Shared.Sets;
using OmniCard.Collection.Sales;
using OmniCard.Web.Api.Controllers;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>Covers the SPA's "list for sale + push to eBay" endpoints on ListingsController: the
/// prepare draft (title/description/categories) and the create path (splits + local listing + eBay
/// push, keeping the local listing when the eBay push fails).</summary>
public class ListingsControllerEbayTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly RecordingEbayListingService _ebay = new();
    private readonly StubEbayAuth _auth = new() { IsConnected = true };
    private readonly ListingsController _controller;

    public ListingsControllerEbayTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();

        var factory = new MockFactory(_opts);
        var listings = new ListingService(factory, new StubSalesSettings());
        var settings = Options.Create(new EbaySettings { Environment = "sandbox" });
        var cards = new WebCardService([new StubMtgGame()]);
        _controller = new ListingsController(listings, factory, new StubPickListPdf(), _ebay, _auth, cards, settings);
    }

    public void Dispose() => _conn.Dispose();

    private static T Value<T>(ActionResult<T> r) => r.Result is ObjectResult o ? (T)o.Value! : r.Value!;

    private int SeedLot(int quantity = 1, string condition = "NM", bool foil = false)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var product = new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Single,
            Name = "Black Lotus",
            SetName = "Alpha",
            SetCode = "LEA",
            CollectorNumber = "232",
            Rarity = "Rare",
            Foil = foil,
            GameCardId = "scryfall-1", // so the catalog image can be hydrated for the eBay photo
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        ctx.StorageContainers.Add(new StorageContainer { Id = 7, Name = "Box 7" });
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = product.Id, Quantity = quantity, LocationId = 7, Condition = condition };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    [Fact]
    public void PrepareEbay_ReturnsSuggestedTitleAndCanonicalSinglesCategory()
    {
        var lotId = SeedLot(condition: "LP", foil: true);

        var draft = Value(_controller.PrepareEbay(lotId));

        Assert.Contains("Black Lotus", draft.SuggestedTitle);
        Assert.Contains("Alpha", draft.SuggestedTitle);
        Assert.Contains("Foil", draft.SuggestedTitle);
        Assert.Equal("LP", draft.Condition);
        Assert.True(draft.IsFoil);
        // Singles always list in the CCG Individual Cards leaf category (183454), not a browse-derived one.
        var cat = Assert.Single(draft.Categories);
        Assert.Equal("183454", cat.CategoryId);
    }

    [Fact]
    public void PrepareEbay_ReturnsNotFound_ForUnknownLot()
    {
        var result = _controller.PrepareEbay(999);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateEbay_ListsLocally_AndPushesToEbay()
    {
        var lotId = SeedLot();

        var res = Value(await _controller.CreateEbay(new CreateEbayListingRequest
        {
            LotId = lotId,
            Quantity = 1,
            Price = 4000m,
            Title = "MTG Black Lotus Alpha #232 NM",
            Description = "A very nice card.",
            Condition = "NM",
            ListingType = "FixedPrice",
            CategoryId = "183454",
        }));

        Assert.True(res.Success);
        Assert.Equal(lotId, res.LotId);
        // The eBay service was called with the card and options carried through.
        Assert.NotNull(_ebay.LastCard);
        Assert.Equal("Black Lotus", _ebay.LastCard!.Name);
        Assert.Equal(4000m, _ebay.LastOptions!.Price);
        Assert.Equal("183454", _ebay.LastOptions.EbayCategoryId);

        // The lot is listed locally on the eBay channel.
        using var ctx = new OmniCardDbContext(_opts);
        var listing = Assert.Single(ctx.Listings.ToList());
        Assert.Equal(SalesChannel.Ebay, listing.Channel);
        Assert.Equal(lotId, listing.LotId);
    }

    [Fact]
    public async Task CreateEbay_HydratesCatalogImage_SoEbayHasAPhoto()
    {
        // The lot has no stored image (scanned/imported). eBay rejects a photo-less publish, so the
        // controller must resolve a public catalog image before pushing.
        var lotId = SeedLot();

        var res = Value(await _controller.CreateEbay(new CreateEbayListingRequest { LotId = lotId, Price = 5m, Condition = "NM" }));

        Assert.True(res.Success);
        Assert.Equal("https://cards.example/scryfall-1.jpg", _ebay.LastCard!.ImageUri);
    }

    [Fact]
    public async Task CreateEbay_ListsMultipleCopies_AsOneMultiQuantityListing()
    {
        var lotId = SeedLot(quantity: 3);

        var res = Value(await _controller.CreateEbay(new CreateEbayListingRequest
        {
            LotId = lotId, Quantity = 3, Price = 5m, Condition = "NM",
        }));

        Assert.True(res.Success);
        Assert.Equal(3, _ebay.LastOptions!.Quantity); // one listing, quantity 3 — not three listings
    }

    [Fact]
    public async Task CreateEbay_MergesIntoExistingListing_ForIdenticalItemAlreadyOnEbay()
    {
        // Lot A (already listed on eBay) and lot B are the same printing + condition (same Product).
        int lotA, lotB;
        using (var ctx = new OmniCardDbContext(_opts))
        {
            var product = new Product
            {
                Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Thranduil, the Elvenking",
                SetName = "The Hobbit", SetCode = "HOB", CollectorNumber = "246", GameCardId = "scryfall-t",
            };
            ctx.Products.Add(product);
            ctx.StorageContainers.Add(new StorageContainer { Id = 7, Name = "Box 7" });
            ctx.SaveChanges();
            var a = new InventoryLot { ProductId = product.Id, Quantity = 1, LocationId = 7, Condition = "NM" };
            var b = new InventoryLot { ProductId = product.Id, Quantity = 1, LocationId = 7, Condition = "NM" };
            ctx.Lots.AddRange(a, b);
            ctx.SaveChanges();
            lotA = a.Id; lotB = b.Id;
            ctx.Listings.Add(new Listing
            {
                LotId = lotA, Channel = SalesChannel.Ebay, Status = ListingStatus.Listed,
                ListedPrice = 50m, Quantity = 1, OriginalLocationId = 7, ListedAt = new DateTime(2026, 1, 1),
            });
            ctx.EbayListings.Add(new EbayListing
            {
                LotId = lotA, EbayItemId = "v1|1|0", Status = EbayListingStatus.Active,
                ListedPrice = 50m, StartTime = new DateTime(2026, 1, 1),
            });
            ctx.SaveChanges();
        }

        var res = Value(await _controller.CreateEbay(new CreateEbayListingRequest
        {
            LotId = lotB, Quantity = 1, Price = 999m, Condition = "NM",
        }));

        Assert.True(res.Success);
        Assert.Equal(lotA, res.LotId);                 // reported against the backing (eBay-owning) lot
        Assert.Equal(2, _ebay.LastOptions!.Quantity);  // combined quantity pushed to eBay
        Assert.Equal(50m, _ebay.LastOptions.Price);    // kept the live listing's price, not B's 999

        using var check = new OmniCardDbContext(_opts);
        // Both cards keep their own lot (and thus their binder slot) — no physical merge.
        Assert.Equal(1, check.Lots.Single(l => l.Id == lotA).Quantity);
        Assert.Equal(1, check.Lots.Single(l => l.Id == lotB).Quantity);
        // B is now listed locally on the eBay channel (covered by A's listing), with no eBay item of its own.
        var bListing = check.Listings.Single(l => l.LotId == lotB && l.Status == ListingStatus.Listed);
        Assert.Equal(SalesChannel.Ebay, bListing.Channel);
        Assert.DoesNotContain(check.EbayListings.ToList(), e => e.LotId == lotB);
        Assert.Single(check.EbayListings.Where(e => e.EbayItemId != "")); // one eBay listing, not two
    }

    [Fact]
    public async Task CreateEbay_StillMerges_WhenExistingListingWasLeftInErrorState()
    {
        // Reproduces the stuck state after a transient failure: the live eBay listing (lot A) was
        // flipped to Error by a prior failed op, and the source lot (B) picked up a stale local listing
        // from a normal-path retry. A further retry must still recognise A's live listing and merge.
        int lotA, lotB;
        using (var ctx = new OmniCardDbContext(_opts))
        {
            var product = new Product
            {
                Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Thranduil, the Elvenking",
                SetName = "The Hobbit", SetCode = "HOB", CollectorNumber = "246", GameCardId = "scryfall-t",
            };
            ctx.Products.Add(product);
            ctx.StorageContainers.Add(new StorageContainer { Id = 7, Name = "Box 7" });
            ctx.SaveChanges();
            var a = new InventoryLot { ProductId = product.Id, Quantity = 1, LocationId = 7, Condition = "NM" };
            var b = new InventoryLot { ProductId = product.Id, Quantity = 1, LocationId = 7, Condition = "NM" };
            ctx.Lots.AddRange(a, b);
            ctx.SaveChanges();
            lotA = a.Id; lotB = b.Id;
            // A: live on eBay but Status=Error (a prior op failed), plus its normal local listing.
            ctx.Listings.Add(new Listing { LotId = lotA, Channel = SalesChannel.Ebay, Status = ListingStatus.Listed, ListedPrice = 50m, Quantity = 1, OriginalLocationId = 7, ListedAt = new DateTime(2026, 1, 1) });
            ctx.EbayListings.Add(new EbayListing { LotId = lotA, EbayItemId = "117427856619", Status = EbayListingStatus.Error, ListedPrice = 50m, ErrorMessage = "Offer update failed", StartTime = new DateTime(2026, 1, 1) });
            // B: stale local listing + errored (never-published) eBay row from a failed normal-path retry.
            ctx.Listings.Add(new Listing { LotId = lotB, Channel = SalesChannel.Ebay, Status = ListingStatus.Listed, ListedPrice = 999m, Quantity = 1, OriginalLocationId = 7, ListedAt = new DateTime(2026, 1, 2) });
            ctx.EbayListings.Add(new EbayListing { LotId = lotB, EbayItemId = "", Status = EbayListingStatus.Error, ListedPrice = 999m });
            ctx.SaveChanges();
        }

        var res = Value(await _controller.CreateEbay(new CreateEbayListingRequest { LotId = lotB, Quantity = 1, Price = 999m, Condition = "NM" }));

        Assert.True(res.Success);
        Assert.Equal(lotA, res.LotId);
        Assert.Equal(2, _ebay.LastOptions!.Quantity);  // quantity-only update to the live listing

        using var check = new OmniCardDbContext(_opts);
        // Both cards survive in their own lots (slots preserved); B stays listed on eBay, covered by A.
        Assert.Equal(1, check.Lots.Single(l => l.Id == lotA).Quantity);
        Assert.Equal(1, check.Lots.Single(l => l.Id == lotB).Quantity);
        Assert.Contains(check.Listings.Where(l => l.LotId == lotB).ToList(),
            l => l.Status == ListingStatus.Listed && l.Channel == SalesChannel.Ebay);
    }

    [Fact]
    public async Task CreateEbay_KeepsLocalListing_WhenEbayPushFails()
    {
        var lotId = SeedLot();
        _ebay.Result = false; // simulate an eBay push failure

        var res = Value(await _controller.CreateEbay(new CreateEbayListingRequest
        {
            LotId = lotId,
            Price = 10m,
            Condition = "NM",
        }));

        Assert.False(res.Success);
        Assert.NotNull(res.Error);
        // The local listing is retained even though eBay failed.
        using var ctx = new OmniCardDbContext(_opts);
        Assert.Single(ctx.Listings.Where(l => l.LotId == lotId).ToList());
    }

    [Fact]
    public async Task Unlist_EndsEbayListing_ForEbayListedLot()
    {
        var lotId = SeedLot();
        // The lot is listed on eBay (local listing + an active EbayListing row the service wrote).
        Value(await _controller.CreateEbay(new CreateEbayListingRequest { LotId = lotId, Price = 5m, Condition = "NM" }));
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.EbayListings.Add(new EbayListing
            {
                LotId = lotId,
                EbayItemId = "v1|123|0",
                Status = EbayListingStatus.Active,
                ListedPrice = 5m,
                StartTime = new DateTime(2026, 1, 1),
            });
            ctx.SaveChanges();
        }

        var result = await _controller.Unlist(lotId);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(lotId, _ebay.EndedLotId); // ended on eBay
        // No active local listing remains.
        using var check = new OmniCardDbContext(_opts);
        Assert.DoesNotContain(check.Listings.ToList(),
            l => l.LotId == lotId && (l.Status == ListingStatus.Listed || l.Status == ListingStatus.Picked));
    }

    [Fact]
    public async Task Unlist_SkipsEbay_WhenLotHasNoEbayListing()
    {
        var lotId = SeedLot();
        // A plain (manual) local listing, no eBay row.
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Listings.Add(new Listing
            {
                LotId = lotId,
                Channel = SalesChannel.Manual,
                Status = ListingStatus.Listed,
                ListedPrice = 1m,
                Quantity = 1,
                OriginalLocationId = 7,
                ListedAt = new DateTime(2026, 1, 1),
            });
            ctx.SaveChanges();
        }

        var result = await _controller.Unlist(lotId);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(_ebay.EndedLotId); // eBay end never attempted
    }

    /// <summary>Seeds two identical eBay-listed copies: lot A owns the eBay item, lot B is a linked copy
    /// (listed on the eBay channel, covered by A's listing, no eBay item of its own).</summary>
    private (int LotA, int LotB) SeedTwoIdenticalEbayCopies()
    {
        using var ctx = new OmniCardDbContext(_opts);
        var product = new Product
        {
            Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Thranduil, the Elvenking",
            SetName = "The Hobbit", SetCode = "HOB", CollectorNumber = "246", GameCardId = "scryfall-t",
        };
        ctx.Products.Add(product);
        ctx.StorageContainers.Add(new StorageContainer { Id = 7, Name = "Box 7" });
        ctx.SaveChanges();
        var a = new InventoryLot { ProductId = product.Id, Quantity = 1, LocationId = 7, Condition = "NM" };
        var b = new InventoryLot { ProductId = product.Id, Quantity = 1, LocationId = 7, Condition = "NM" };
        ctx.Lots.AddRange(a, b);
        ctx.SaveChanges();
        ctx.Listings.Add(new Listing { LotId = a.Id, Channel = SalesChannel.Ebay, Status = ListingStatus.Listed, ListedPrice = 50m, Quantity = 1, OriginalLocationId = 7, ListedAt = new DateTime(2026, 1, 1) });
        ctx.Listings.Add(new Listing { LotId = b.Id, Channel = SalesChannel.Ebay, Status = ListingStatus.Listed, ListedPrice = 50m, Quantity = 1, OriginalLocationId = 7, ListedAt = new DateTime(2026, 1, 2) });
        ctx.EbayListings.Add(new EbayListing { LotId = a.Id, EbayItemId = "117427856619", Status = EbayListingStatus.Active, ListedPrice = 50m, StartTime = new DateTime(2026, 1, 1) });
        ctx.SaveChanges();
        return (a.Id, b.Id);
    }

    [Fact]
    public async Task Unlist_ReducesEbayQuantity_WhenOtherIdenticalCopiesRemain()
    {
        var (lotA, lotB) = SeedTwoIdenticalEbayCopies();

        var result = await _controller.Unlist(lotB);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(_ebay.EndedLotId);                 // listing not ended — other copies remain
        Assert.Equal(1, _ebay.LastOptions!.Quantity);  // reduced to the remaining copy
        using var check = new OmniCardDbContext(_opts);
        Assert.True(check.Lots.Any(l => l.Id == lotB));  // B's lot (and slot) survives
        Assert.DoesNotContain(check.Listings.Where(l => l.LotId == lotB).ToList(),
            l => l.Status == ListingStatus.Listed || l.Status == ListingStatus.Picked);
        Assert.Contains(check.Listings.Where(l => l.LotId == lotA).ToList(), l => l.Status == ListingStatus.Listed);
    }

    [Fact]
    public async Task Unlist_EndsEbayListing_WhenRemovingTheLastCopy()
    {
        var (lotA, lotB) = SeedTwoIdenticalEbayCopies();
        await _controller.Unlist(lotB); // now only A remains

        var result = await _controller.Unlist(lotA);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(lotA, _ebay.EndedLotId); // last copy → whole eBay listing ended
    }

    [Fact]
    public async Task GetDetails_IncludesEbayItemIdAndViewUrl_ForEbayListedLot()
    {
        var lotId = SeedLot();
        Value(await _controller.CreateEbay(new CreateEbayListingRequest { LotId = lotId, Price = 5m, Condition = "NM" }));
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.EbayListings.Add(new EbayListing
            {
                LotId = lotId,
                EbayItemId = "v1|1234567890|0",
                Status = EbayListingStatus.Active,
                ListedPrice = 5m,
                StartTime = new DateTime(2026, 1, 1),
            });
            ctx.SaveChanges();
        }

        var details = Value(_controller.GetDetails(null));
        var row = Assert.Single(details, d => d.LotId == lotId);
        Assert.Equal("v1|1234567890|0", row.EbayItemId);
        Assert.Equal("Active", row.EbayStatus);
        Assert.Equal("https://www.sandbox.ebay.com/itm/v1|1234567890|0", row.EbayViewUrl);
    }

    [Fact]
    public async Task ReviseEbay_RepushesOffer_AndSyncsLocalPrice()
    {
        var lotId = SeedLot();
        var res = Value(await _controller.CreateEbay(new CreateEbayListingRequest { LotId = lotId, Price = 5m, Condition = "NM" }));
        Assert.True(res.Success);
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.EbayListings.Add(new EbayListing
            {
                LotId = lotId,
                EbayItemId = "v1|1|0",
                Status = EbayListingStatus.Active,
                ListedPrice = 5m,
                StartTime = new DateTime(2026, 1, 1),
            });
            ctx.SaveChanges();
        }
        int listingId;
        using (var ctx = new OmniCardDbContext(_opts))
            listingId = ctx.Listings.Single(l => l.LotId == lotId).Id;

        var revised = Value(await _controller.ReviseEbay(new ReviseEbayListingRequest
        {
            ListingId = listingId,
            LotId = lotId,
            Price = 42m,
            Title = "Updated title",
            Description = "Updated desc",
            Condition = "LP",
        }));

        Assert.True(revised.Success);
        Assert.Equal(42m, _ebay.LastOptions!.Price); // new price pushed to eBay
        Assert.Equal("Updated title", _ebay.LastOptions.Title);
        // Local listing price kept in sync.
        using var check = new OmniCardDbContext(_opts);
        Assert.Equal(42m, check.Listings.Single(l => l.Id == listingId).ListedPrice);
    }

    [Fact]
    public async Task ReviseEbay_ReturnsNotFound_WhenNoActiveEbayListing()
    {
        var lotId = SeedLot();
        // Listed locally (manual) but never pushed to eBay → no active EbayListing.
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Listings.Add(new Listing
            {
                LotId = lotId,
                Channel = SalesChannel.Manual,
                Status = ListingStatus.Listed,
                ListedPrice = 1m,
                Quantity = 1,
                OriginalLocationId = 7,
                ListedAt = new DateTime(2026, 1, 1),
            });
            ctx.SaveChanges();
        }

        var result = await _controller.ReviseEbay(new ReviseEbayListingRequest { LotId = lotId, Price = 2m });
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateEbay_ReturnsBadRequest_WhenNotConnected()
    {
        var lotId = SeedLot();
        _auth.IsConnected = false;

        var result = await _controller.CreateEbay(new CreateEbayListingRequest { LotId = lotId, Price = 1m });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        // Nothing listed locally when we bail before listing.
        using var ctx = new OmniCardDbContext(_opts);
        Assert.Empty(ctx.Listings.ToList());
    }

    // --- stubs ---

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> o) : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }

    private sealed class RecordingEbayListingService : IEbayListingService
    {
        public bool Result { get; set; } = true;
        public CollectionCard? LastCard { get; private set; }
        public EbayListingOptions? LastOptions { get; private set; }
        public int? EndedLotId { get; private set; }

        public Task<bool> CreateListingAsync(CollectionCard card, EbayListingOptions options)
        {
            LastCard = card;
            LastOptions = options;
            return Task.FromResult(Result);
        }
        public Task<bool> CreateSealedListingAsync(Product product, int lotId, EbayListingOptions options) => Task.FromResult(Result);
        public Task<bool> ReviseListingAsync(EbayListing listing, EbayListingOptions options) => Task.FromResult(Result);
        public Task<bool> UpdateQuantityAsync(CollectionCard card, EbayListingOptions options)
        {
            LastCard = card;
            LastOptions = options;
            return Task.FromResult(Result);
        }
        public Task<bool> EndListingAsync(EbayListing listing)
        {
            EndedLotId = listing.LotId;
            return Task.FromResult(true);
        }
        public Task<List<EbaySellerPolicy>> GetSellerPoliciesAsync(string policyType) => Task.FromResult(new List<EbaySellerPolicy>());
    }

    // Minimal MTG catalog stub: FindCardById returns a card with a public image so the controller can
    // hydrate a photo for the eBay listing. Everything else is a no-op.
    private sealed class StubMtgGame : ICardGameService
    {
        public CardGame Game => CardGame.Mtg;
        public MatchDiagnostics? LastMatchDiagnostics => null;
        public object? FindCardById(string gameCardId) =>
            new Card { ImageUris = new ImageUris { Normal = $"https://cards.example/{gameCardId}.jpg" } };
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null,
            IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) => null;
        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => null;
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => new();
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => [];
        public List<CardMatch> GetPrintings(string cardName) => [];
        public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
        public IReadOnlyList<SetInfo> GetAvailableSets() => [];
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
        public List<SetCatalogCard> GetSetCards(string setCode) => [];
    }

    private sealed class StubEbayAuth : IEbayAuthService
    {
        public bool IsConnected { get; set; }
        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("token");
        public Task<bool> ExchangeCodeForTokensAsync(string authCode) => Task.FromResult(true);
        public void Disconnect() { }
        public string GetAuthorizationUrl() => "https://ebay.example/auth";
        public IReadOnlyList<string> GetMissingConfiguration() => [];
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Touch() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
    }

    private sealed class StubPickListPdf : IPickListPdfExporter
    {
        public void Export(IReadOnlyList<PickListEntry> entries, string filePath) { }
    }

    private sealed class StubSalesSettings : ISalesSettingsService
    {
        public int? ForSaleLocationId { get; private set; } = 7;
        public void SetForSaleLocationId(int? id) => ForSaleLocationId = id;
        public bool MovePickedToForSaleLocation { get; private set; } = true;
        public void SetMovePickedToForSaleLocation(bool move) => MovePickedToForSaleLocation = move;
        public CompanyProfile GetCompany() => new();
        public void SaveCompany(CompanyProfile company) { }
        public ReceiptSettings GetReceipt() => new();
        public void SaveReceipt(ReceiptSettings receipt) { }
        public string SetLogo(string sourcePath) => "company-logo.png";
        public double? OrdersEditorWidth => null;
        public void SetOrdersEditorWidth(double width) { }
        public bool OrdersEditorCollapsed => false;
        public void SetOrdersEditorCollapsed(bool collapsed) { }
        public IReadOnlyList<WorkflowLane> GetWorkflowLanes() => WorkflowLane.Defaults();
        public void SaveWorkflowLanes(IEnumerable<WorkflowLane> lanes) { }
    }
}
