using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.eBay;

namespace OmniCard.Tests.Services;

public class EbaySellerSetupServiceTests
{
    private readonly EbaySettings _settings = new() { Environment = "sandbox", AppId = "a", CertId = "c" };

    // In-memory settings service.
    private sealed class MemSettings : IEbaySellingSettingsService
    {
        public EbaySellingSettings Current = new();
        public EbaySellingSettings Get() => Current;
        public void Save(EbaySellingSettings settings) => Current = settings;
        public bool IsSetupComplete() =>
            Current.LocationProvisioned && !string.IsNullOrEmpty(Current.FulfillmentPolicyId) && !string.IsNullOrEmpty(Current.ReturnPolicyId);
    }

    // Routes canned responses by (method, path substring). Records requests.
    private sealed class RoutingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri, string? Body)> Requests { get; } = [];
        private readonly Func<HttpRequestMessage, string, (HttpStatusCode, string)> _route;
        public RoutingHandler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> route) => _route = route;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.ToString(), body));
            var (code, resp) = _route(request, body ?? "");
            return new HttpResponseMessage(code) { Content = new StringContent(resp, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _h;
        public SingleClientFactory(HttpMessageHandler h) => _h = h;
        public HttpClient CreateClient(string name) => new(_h, disposeHandler: false);
    }

    private EbaySellerSetupService Create(RoutingHandler handler, MemSettings settings) =>
        new(Options.Create(_settings), new SingleClientFactory(handler),
            new FakeEbayAuthService("token"), settings, NullLogger<EbaySellerSetupService>.Instance);

    private static MemSettings WithValidAddress()
    {
        var m = new MemSettings();
        m.Current.AddressLine1 = "1 Main St";
        m.Current.City = "Portland";
        m.Current.State = "OR";
        m.Current.PostalCode = "97201";
        m.Current.Country = "US";
        return m;
    }

    [Fact]
    public async Task RunSetup_OptIn_And_CreatesLocation_WhenMissing()
    {
        var settings = WithValidAddress();
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Get) return (HttpStatusCode.NotFound, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Post) return (HttpStatusCode.NoContent, "");
            // policies: return empty lists then create
            if (u.EndsWith("_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, "{}");
            if (u.Contains("fulfillment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { fulfillmentPolicyId = "fp-1" }));
            if (u.Contains("return_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { returnPolicyId = "rp-1" }));
            if (u.Contains("payment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { paymentPolicyId = "pp-1" }));
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        // Policy steps + result.Success are implemented in Task 3 (FinalizeAsync is a stub here).
        Assert.True(settings.Current.LocationProvisioned);
        // Location POST was made to the merchant key
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.Contains("/location/omnicard-primary"));
    }

    [Fact]
    public async Task RunSetup_LocationStepFails_WhenAddressMissing()
    {
        var settings = new MemSettings(); // no address
        var handler = new RoutingHandler((req, body) =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        var loc = result.Steps.Single(s => s.Name == "Inventory location");
        Assert.Equal(EbaySetupStepStatus.Failed, loc.Status);
        Assert.False(result.Success);
        // No location POST attempted without required fields
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.Contains("/location/"));
    }

    [Fact]
    public async Task RunSetup_LocationStepFails_WhenLocationGetErrors()
    {
        var settings = WithValidAddress(); // valid address → not the missing-address short-circuit
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            // GET location returns a real error (not 404) — must fail closed, not create.
            if (u.Contains("/location/") && req.Method == HttpMethod.Get) return (HttpStatusCode.InternalServerError, "{\"errors\":[]}");
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        var loc = result.Steps.Single(s => s.Name == "Inventory location");
        Assert.Equal(EbaySetupStepStatus.Failed, loc.Status);
        // A non-404 GET error must NOT fall through to a create POST.
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.Contains("/location/"));
    }

    [Fact]
    public async Task RunSetup_SkipsLocationCreate_WhenAlreadyExists()
    {
        var settings = WithValidAddress();
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { merchantLocationKey = "omnicard-primary" }));
            if (u.EndsWith("_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, "{}");
            if (u.Contains("fulfillment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { fulfillmentPolicyId = "fp-1" }));
            if (u.Contains("return_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { returnPolicyId = "rp-1" }));
            if (u.Contains("payment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { paymentPolicyId = "pp-1" }));
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        var loc = result.Steps.Single(s => s.Name == "Inventory location");
        Assert.Equal(EbaySetupStepStatus.SkippedExisting, loc.Status);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.Contains("/location/"));
    }
}
