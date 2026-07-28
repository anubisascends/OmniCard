using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.eBay;
using System.Net;
using System.Text.Json;

namespace OmniCard.Tests.Services;

public class EbayListingServiceTests : IDisposable
{
    private sealed class StubSellingSettings : IEbaySellingSettingsService
    {
        private readonly EbaySellingSettings _s;
        public StubSellingSettings(EbaySellingSettings s) => _s = s;
        public EbaySellingSettings Get() => _s;
        public void Save(EbaySellingSettings settings) { }
        public bool IsSetupComplete() =>
            _s.LocationProvisioned && !string.IsNullOrEmpty(_s.FulfillmentPolicyId) && !string.IsNullOrEmpty(_s.ReturnPolicyId);
    }

    private static StubSellingSettings CompleteSellingSettings() => new(new EbaySellingSettings
    {
        MerchantLocationKey = "omnicard-primary",
        LocationProvisioned = true,
        FulfillmentPolicyId = "fp-1",
        ReturnPolicyId = "rp-1",
        PaymentPolicyId = "pp-1",
    });

    private readonly EbaySettings _settings = new()
    {
        AppId = "test-app-id",
        CertId = "test-cert-id",
        Environment = "sandbox",
    };

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;

    public EbayListingServiceTests()
    {
        // Real SQLite (not InMemory) so the EbayListing -> InventoryLot FK/cascade configured in
        // OmniCardDbContext is actually enforced by the database, matching production behavior.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new OmniCardDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private IDbContextFactory<OmniCardDbContext> CreateDbFactory() => new TestDbContextFactory(_options);

    private static int SeedLot(OmniCardDbContext ctx, string name = "Black Lotus")
    {
        var product = new Product
        {
            Game = CardGame.Mtg, Category = ProductCategory.Single, Name = name,
            SetName = "Alpha", SetCode = "LEA", CollectorNumber = "232", Rarity = "Rare",
            GameCardId = "scryfall-123",
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();

        var lot = new InventoryLot { ProductId = product.Id, Quantity = 1 };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    [Fact]
    public async Task CreateListingAsync_SavesEbayListing_WhenApiSucceeds()
    {
        var dbFactory = CreateDbFactory();
        int lotId;
        using (var ctx = dbFactory.CreateDbContext())
            lotId = SeedLot(ctx);

        var responseJson = JsonSerializer.Serialize(new { listingId = "ebay-item-12345" });
        var handler = new FakeHttpHandler(HttpStatusCode.OK, responseJson);
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");

        var svc = new EbayListingService(
            Options.Create(_settings),
            factory,
            authService,
            dbFactory,
            CompleteSellingSettings(),
            NullLogger<EbayListingService>.Instance);

        var options = new EbayListingOptions
        {
            Title = "MTG Black Lotus [LEA] #232 NM",
            Description = "Near Mint Black Lotus from Alpha",
            Price = 5000m,
            ListingType = EbayListingType.FixedPrice,
        };

        // The DTO's Id is the LotId (per the unified read facade); CreateListingAsync doesn't
        // require the CollectionCard itself to be persisted anywhere.
        var card = new CollectionCard { Id = lotId, Name = "Black Lotus" };
        var result = await svc.CreateListingAsync(card, options);

        Assert.True(result);

        using var verifyCtx = dbFactory.CreateDbContext();
        var listing = verifyCtx.EbayListings.FirstOrDefault(l => l.LotId == lotId);
        Assert.NotNull(listing);
        Assert.Equal(EbayListingStatus.Active, listing.Status);
        Assert.Equal(5000m, listing.ListedPrice);
    }

    [Fact]
    public async Task CreateListingAsync_SendsContentLanguageHeader_OnInventoryAndOffer()
    {
        // Regression: eBay's Inventory API rejects createOffer with errorId 25709
        // ("Invalid value for header Content-Language") unless the header is set.
        var dbFactory = CreateDbFactory();
        int lotId;
        using (var ctx = dbFactory.CreateDbContext())
            lotId = SeedLot(ctx);

        var responseJson = JsonSerializer.Serialize(new { listingId = "ebay-item-12345" });
        var handler = new RecordingHttpHandler(HttpStatusCode.OK, responseJson);
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");

        var svc = new EbayListingService(
            Options.Create(_settings), factory, authService, dbFactory,
            CompleteSellingSettings(),
            NullLogger<EbayListingService>.Instance);

        var options = new EbayListingOptions
        {
            Title = "MTG Black Lotus [LEA] #232 NM",
            Description = "Near Mint Black Lotus from Alpha",
            Price = 5000m,
            ListingType = EbayListingType.FixedPrice,
        };
        var card = new CollectionCard { Id = lotId, Name = "Black Lotus" };

        var result = await svc.CreateListingAsync(card, options);
        Assert.True(result);

        var mutating = handler.Requests
            .Where(r => r.Method == HttpMethod.Put || r.Method == HttpMethod.Post)
            .ToList();
        Assert.NotEmpty(mutating);
        foreach (var req in mutating)
        {
            Assert.True(
                req.ContentLanguage.Contains("en-US"),
                $"{req.Method} {req.Uri} is missing Content-Language: en-US");
        }
    }

    [Fact]
    public async Task EndListingAsync_UpdatesStatusToEnded()
    {
        var dbFactory = CreateDbFactory();
        int lotId;
        using (var ctx = dbFactory.CreateDbContext())
        {
            lotId = SeedLot(ctx, "Test");
            ctx.EbayListings.Add(new EbayListing
            {
                LotId = lotId, EbayItemId = "ebay-123",
                Status = EbayListingStatus.Active, ListedPrice = 10m,
            });
            ctx.SaveChanges();
        }

        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{}");
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");

        var svc = new EbayListingService(
            Options.Create(_settings),
            factory,
            authService,
            dbFactory,
            CompleteSellingSettings(),
            NullLogger<EbayListingService>.Instance);

        var listing = dbFactory.CreateDbContext().EbayListings.First(l => l.LotId == lotId);
        var result = await svc.EndListingAsync(listing);

        Assert.True(result);

        using var verifyCtx = dbFactory.CreateDbContext();
        var updated = verifyCtx.EbayListings.First(l => l.LotId == lotId);
        Assert.Equal(EbayListingStatus.Ended, updated.Status);
    }

    [Fact]
    public async Task GetSellerPoliciesAsync_ReturnsPolicies()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            fulfillmentPolicies = new[]
            {
                new { fulfillmentPolicyId = "policy-1", name = "Standard Shipping" }
            }
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, responseJson);
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");
        var dbFactory = CreateDbFactory();

        var svc = new EbayListingService(
            Options.Create(_settings),
            factory,
            authService,
            dbFactory,
            CompleteSellingSettings(),
            NullLogger<EbayListingService>.Instance);

        var policies = await svc.GetSellerPoliciesAsync("fulfillment");

        Assert.Single(policies);
        Assert.Equal("policy-1", policies[0].PolicyId);
        Assert.Equal("Standard Shipping", policies[0].Name);
    }

    [Fact]
    public async Task CreateListingAsync_OfferIncludesMerchantLocationKey_AndStoredPolicies()
    {
        var dbFactory = CreateDbFactory();
        int lotId;
        using (var ctx = dbFactory.CreateDbContext()) lotId = SeedLot(ctx);

        var handler = new RecordingHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(new { listingId = "L1" }));
        var selling = new StubSellingSettings(new EbaySellingSettings
        {
            MerchantLocationKey = "omnicard-primary",
            LocationProvisioned = true,
            FulfillmentPolicyId = "fp-1",
            ReturnPolicyId = "rp-1",
            PaymentPolicyId = "pp-1",
        });

        var svc = new EbayListingService(
            Options.Create(_settings), new FakeHttpClientFactory(handler),
            new FakeEbayAuthService("t"), dbFactory, selling,
            NullLogger<EbayListingService>.Instance);

        var options = new EbayListingOptions { Title = "t", Description = "d", Price = 5m, ListingType = EbayListingType.FixedPrice };
        var ok = await svc.CreateListingAsync(new CollectionCard { Id = lotId, Name = "n" }, options);

        Assert.True(ok);
        var offerReq = handler.Requests.First(r => r.Method == HttpMethod.Post && r.Uri!.ToString().EndsWith("/offer"));
        Assert.Contains("omnicard-primary", offerReq.Body);
        Assert.Contains("fp-1", offerReq.Body);
    }

    [Fact]
    public async Task CreateListingAsync_InventoryItem_UsesUngradedCardConditionAndGradeDescriptor()
    {
        // Regression: category 183454 (CCG singles) rejects NEW_OTHER (1500). Must send
        // USED_VERY_GOOD (ungraded) plus the Card Condition descriptor (name 40001).
        var dbFactory = CreateDbFactory();
        int lotId;
        using (var ctx = dbFactory.CreateDbContext()) lotId = SeedLot(ctx);

        var handler = new RecordingHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(new { listingId = "L1" }));
        var svc = new EbayListingService(
            Options.Create(_settings), new FakeHttpClientFactory(handler),
            new FakeEbayAuthService("t"), dbFactory, CompleteSellingSettings(),
            NullLogger<EbayListingService>.Instance);

        var ok = await svc.CreateListingAsync(new CollectionCard { Id = lotId, Name = "n" },
            new EbayListingOptions { Title = "t", Description = "d", Price = 5m, Condition = "NM", ListingType = EbayListingType.FixedPrice });

        Assert.True(ok);
        var inv = handler.Requests.First(r => r.Method == HttpMethod.Put && r.Uri!.ToString().Contains("/inventory_item/"));
        Assert.Contains("USED_VERY_GOOD", inv.Body);
        Assert.Contains("40001", inv.Body);
        Assert.Contains("400010", inv.Body); // NM → Near Mint or Better
        Assert.DoesNotContain("NEW_OTHER", inv.Body);
    }

    [Fact]
    public async Task CreateListingAsync_Fails_WhenSetupIncomplete()
    {
        var dbFactory = CreateDbFactory();
        int lotId;
        using (var ctx = dbFactory.CreateDbContext()) lotId = SeedLot(ctx);

        var handler = new RecordingHttpHandler(HttpStatusCode.OK, "{}");
        var selling = new StubSellingSettings(new EbaySellingSettings()); // not provisioned

        var svc = new EbayListingService(
            Options.Create(_settings), new FakeHttpClientFactory(handler),
            new FakeEbayAuthService("t"), dbFactory, selling,
            NullLogger<EbayListingService>.Instance);

        var ok = await svc.CreateListingAsync(new CollectionCard { Id = lotId, Name = "n" },
            new EbayListingOptions { Title = "t", Price = 5m });

        Assert.False(ok);
        Assert.Empty(handler.Requests.Where(r => r.Uri!.ToString().Contains("inventory_item")));
    }

    [Fact]
    public async Task CreateListingAsync_UpdatesAndPublishesExistingOffer_WhenOfferAlreadyExists()
    {
        // Regression: eBay allows one offer per SKU. A prior unpublished attempt leaves an
        // offer behind; recreating fails with 25002 "Offer entity already exists". The service
        // must find that offer, update it, and publish — not POST a new one.
        var dbFactory = CreateDbFactory();
        int lotId;
        using (var ctx = dbFactory.CreateDbContext()) lotId = SeedLot(ctx);

        var handler = new RoutingRecordingHttpHandler((method, uri) =>
        {
            if (method == HttpMethod.Get && uri.Contains("/offer?sku="))
                return (HttpStatusCode.OK, JsonSerializer.Serialize(new { offers = new[] { new { offerId = "existing-9" } } }));
            if (method == HttpMethod.Put && uri.Contains("/inventory_item/"))
                return (HttpStatusCode.OK, "{}");
            if (method == HttpMethod.Put && uri.Contains("/offer/existing-9"))
                return (HttpStatusCode.NoContent, "");
            if (method == HttpMethod.Post && uri.Contains("/offer/existing-9/publish"))
                return (HttpStatusCode.OK, JsonSerializer.Serialize(new { listingId = "LPUB" }));
            return (HttpStatusCode.OK, "{}");
        });

        var svc = new EbayListingService(
            Options.Create(_settings), new FakeHttpClientFactory(handler),
            new FakeEbayAuthService("t"), dbFactory, CompleteSellingSettings(),
            NullLogger<EbayListingService>.Instance);

        var ok = await svc.CreateListingAsync(new CollectionCard { Id = lotId, Name = "n" },
            new EbayListingOptions { Title = "t", Description = "d", Price = 5m, ListingType = EbayListingType.FixedPrice });

        Assert.True(ok);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Put && r.Uri!.ToString().Contains("/offer/existing-9"));
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri!.ToString().EndsWith("/offer"));
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri!.ToString().Contains("/offer/existing-9/publish"));

        using var verifyCtx = dbFactory.CreateDbContext();
        var listing = verifyCtx.EbayListings.First(l => l.LotId == lotId);
        Assert.Equal(EbayListingStatus.Active, listing.Status);
        Assert.Equal("LPUB", listing.EbayItemId);
    }

    [Fact]
    public void DeletingLot_CascadesEbayListing()
    {
        var dbFactory = CreateDbFactory();
        int lotId;
        using (var ctx = dbFactory.CreateDbContext())
        {
            lotId = SeedLot(ctx, "Cascade Test");
            ctx.EbayListings.Add(new EbayListing
            {
                LotId = lotId, EbayItemId = "ebay-cascade", Status = EbayListingStatus.Active, ListedPrice = 1m,
            });
            ctx.SaveChanges();
        }

        using (var ctx = dbFactory.CreateDbContext())
        {
            var lot = ctx.Lots.Single(l => l.Id == lotId);
            ctx.Lots.Remove(lot);
            ctx.SaveChanges();
        }

        using var verifyCtx = dbFactory.CreateDbContext();
        Assert.Empty(verifyCtx.EbayListings.Where(l => l.LotId == lotId));
    }
}

/// <summary>
/// Captures a snapshot of each outgoing request (method, uri, Content-Language)
/// so assertions can inspect headers after HttpClient has disposed the content.
/// </summary>
public class RecordingHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;
    public List<RecordedRequest> Requests { get; } = [];

    public RecordingHttpHandler(HttpStatusCode status, string body)
    {
        _status = status;
        _body = body;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var contentLanguage = request.Content?.Headers.ContentLanguage.ToList() ?? [];
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri, contentLanguage, body));
        return new HttpResponseMessage(_status)
        {
            Content = new System.Net.Http.StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}

public record RecordedRequest(HttpMethod Method, Uri? Uri, List<string> ContentLanguage, string? Body = null);

/// <summary>
/// Records requests and returns a per-request routed response keyed by (method, uri).
/// </summary>
public class RoutingRecordingHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpMethod, string, (HttpStatusCode, string)> _route;
    public List<RecordedRequest> Requests { get; } = [];

    public RoutingRecordingHttpHandler(Func<HttpMethod, string, (HttpStatusCode, string)> route) => _route = route;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var contentLanguage = request.Content?.Headers.ContentLanguage.ToList() ?? [];
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri, contentLanguage, body));
        var (code, resp) = _route(request.Method, request.RequestUri!.ToString());
        return new HttpResponseMessage(code)
        {
            Content = new System.Net.Http.StringContent(resp, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}

public class TestDbContextFactory : IDbContextFactory<OmniCardDbContext>
{
    private readonly DbContextOptions<OmniCardDbContext> _options;
    public TestDbContextFactory(DbContextOptions<OmniCardDbContext> options) => _options = options;
    public OmniCardDbContext CreateDbContext() => new(_options);
}
