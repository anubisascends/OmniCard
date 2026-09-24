using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.eBay;
using OmniCard.Shared.Ebay;

namespace OmniCard.Tests.Services.Ebay;

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

        Assert.True(result.Success);
        Assert.True(settings.Current.LocationProvisioned);
        // Location POST was made to the merchant key
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.Contains("/location/omnicard-primary"));
    }

    [Fact]
    public async Task RunSetup_PolicyPayloads_UseValidEbayFields()
    {
        // Regression for live-sandbox failures: fulfillment uses the configured shipping-service
        // code (default USPSPriority; USPSGround/USPSGroundAdvantage were rejected in sandbox) and
        // the return policy must include refundMethod=MONEY_BACK (omitting it caused
        // "some fields missed" / policy-dependency errors).
        var settings = WithValidAddress();
        settings.Current.ReturnsAccepted = true;
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Get) return (HttpStatusCode.NotFound, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Post) return (HttpStatusCode.NoContent, "");
            if (u.EndsWith("_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, "{}");
            if (u.Contains("fulfillment_policy")) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { fulfillmentPolicyId = "fp-1" }));
            if (u.Contains("return_policy")) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { returnPolicyId = "rp-1" }));
            if (u.Contains("payment_policy")) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { paymentPolicyId = "pp-1" }));
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        await svc.RunSetupAsync();

        var fulfillmentPost = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri.EndsWith("fulfillment_policy"));
        Assert.Contains("USPSPriority", fulfillmentPost.Body);
        // eBay rejects a fulfillment policy whose globalShipping is null (errorId 20403).
        Assert.Contains("globalShipping", fulfillmentPost.Body);

        var paymentPost = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri.EndsWith("payment_policy"));
        // eBay-managed payments require immediatePay (errorId 20403 "Immediate Pay is required").
        Assert.Contains("immediatePay", paymentPost.Body);

        var returnPost = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri.EndsWith("return_policy"));
        Assert.Contains("MONEY_BACK", returnPost.Body);
        Assert.Contains("returnShippingCostPayer", returnPost.Body);
    }

    [Fact]
    public async Task RunSetup_ReturnPolicy_OmitsPeriodAndPayer_WhenReturnsNotAccepted()
    {
        var settings = WithValidAddress();
        settings.Current.ReturnsAccepted = false;
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Get) return (HttpStatusCode.NotFound, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Post) return (HttpStatusCode.NoContent, "");
            if (u.EndsWith("_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, "{}");
            if (u.Contains("fulfillment_policy")) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { fulfillmentPolicyId = "fp-1" }));
            if (u.Contains("return_policy")) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { returnPolicyId = "rp-1" }));
            if (u.Contains("payment_policy")) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { paymentPolicyId = "pp-1" }));
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        await svc.RunSetupAsync();

        var returnPost = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri.EndsWith("return_policy"));
        Assert.Contains("\"returnsAccepted\":false", returnPost.Body!.Replace(" ", ""));
        Assert.DoesNotContain("returnShippingCostPayer", returnPost.Body);
        Assert.DoesNotContain("returnPeriod", returnPost.Body);
    }

    [Fact]
    public async Task RunSetup_OptIn_AlreadyExists_25804_IsSkippedNotFailed()
    {
        // Live sandbox returns errorId 25804 "programType already exists" (not the phrase
        // "already opted in") when the account is already enrolled — must be SkippedExisting.
        var settings = WithValidAddress();
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in"))
                return (HttpStatusCode.Conflict, JsonSerializer.Serialize(new { errors = new[] { new { errorId = 25804, message = "programType already exists" } } }));
            if (u.Contains("/location/") && req.Method == HttpMethod.Get) return (HttpStatusCode.NotFound, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Post) return (HttpStatusCode.NoContent, "");
            if (u.EndsWith("_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, "{}");
            if (u.Contains("fulfillment_policy")) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { fulfillmentPolicyId = "fp-1" }));
            if (u.Contains("return_policy")) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { returnPolicyId = "rp-1" }));
            if (u.Contains("payment_policy")) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { paymentPolicyId = "pp-1" }));
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        var optIn = result.Steps.Single(s => s.Name == "Business Policies opt-in");
        Assert.Equal(EbaySetupStepStatus.SkippedExisting, optIn.Status);
        Assert.True(result.Success);
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

    [Fact]
    public async Task RunSetup_CreatesPolicies_AndStoresIds()
    {
        var settings = WithValidAddress();
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Get) return (HttpStatusCode.NotFound, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Post) return (HttpStatusCode.NoContent, "");
            if (u.EndsWith("fulfillment_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { fulfillmentPolicies = Array.Empty<object>() }));
            if (u.EndsWith("return_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { returnPolicies = Array.Empty<object>() }));
            if (u.EndsWith("payment_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { paymentPolicies = Array.Empty<object>() }));
            if (u.EndsWith("fulfillment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { fulfillmentPolicyId = "fp-1" }));
            if (u.EndsWith("return_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { returnPolicyId = "rp-1" }));
            if (u.EndsWith("payment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { paymentPolicyId = "pp-1" }));
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        Assert.True(result.Success);
        Assert.Equal("fp-1", settings.Current.FulfillmentPolicyId);
        Assert.Equal("rp-1", settings.Current.ReturnPolicyId);
        Assert.Equal("pp-1", settings.Current.PaymentPolicyId);
        Assert.NotNull(settings.Current.SetupCompletedAt);
    }

    [Fact]
    public async Task RunSetup_ReusesExistingPolicy_ByName()
    {
        var settings = WithValidAddress();
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            if (u.Contains("/location/")) return (req.Method == HttpMethod.Get) ? (HttpStatusCode.OK, JsonSerializer.Serialize(new { merchantLocationKey = "omnicard-primary" })) : (HttpStatusCode.NoContent, "");
            if (u.EndsWith("fulfillment_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { fulfillmentPolicies = new[] { new { fulfillmentPolicyId = "fp-existing", name = "OmniCard Default" } } }));
            if (u.EndsWith("return_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { returnPolicies = new[] { new { returnPolicyId = "rp-existing", name = "OmniCard Default" } } }));
            if (u.EndsWith("payment_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { paymentPolicies = new[] { new { paymentPolicyId = "pp-existing", name = "OmniCard Default" } } }));
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        Assert.Equal("fp-existing", settings.Current.FulfillmentPolicyId);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.EndsWith("fulfillment_policy"));
    }

    [Fact]
    public async Task RunSetup_UpdatesExistingFulfillmentPolicy_WithCurrentShippingCost()
    {
        // Regression: previously an existing policy was skipped without updating, so edits to the
        // shipping cost never reached eBay and listings kept the stale cost. Now the existing policy
        // is PUT-updated with the current settings.
        var settings = WithValidAddress();
        settings.Current.FreeShipping = false;
        settings.Current.ShippingCost = 11.99m;
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            if (u.Contains("/location/")) return (req.Method == HttpMethod.Get) ? (HttpStatusCode.OK, JsonSerializer.Serialize(new { merchantLocationKey = "omnicard-primary" })) : (HttpStatusCode.NoContent, "");
            if (u.EndsWith("fulfillment_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { fulfillmentPolicies = new[] { new { fulfillmentPolicyId = "fp-existing", name = "OmniCard Default" } } }));
            if (u.EndsWith("return_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { returnPolicies = new[] { new { returnPolicyId = "rp-existing", name = "OmniCard Default" } } }));
            if (u.EndsWith("payment_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { paymentPolicies = new[] { new { paymentPolicyId = "pp-existing", name = "OmniCard Default" } } }));
            // PUT (update) to any policy succeeds.
            if (u.Contains("_policy/") && req.Method == HttpMethod.Put) return (HttpStatusCode.OK, "{}");
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        await svc.RunSetupAsync();

        var put = handler.Requests.Single(r => r.Method == HttpMethod.Put && r.Uri.EndsWith("fulfillment_policy/fp-existing"));
        Assert.Contains("11.99", put.Body);
        // The existing policy id is still what listings use — no duplicate created.
        Assert.Equal("fp-existing", settings.Current.FulfillmentPolicyId);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.EndsWith("fulfillment_policy"));
    }

    [Fact]
    public async Task RunSetup_NoOpPolicyUpdate_IsSkippedExisting_NotFailed()
    {
        // eBay rejects an identical (no-op) policy update with errorId 20403 "Business Profile
        // information in the request is the same as in the system". That must be treated as success
        // (the policy already matches), not a failed step.
        var settings = WithValidAddress();
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            if (u.Contains("/location/")) return (req.Method == HttpMethod.Get) ? (HttpStatusCode.OK, JsonSerializer.Serialize(new { merchantLocationKey = "omnicard-primary" })) : (HttpStatusCode.NoContent, "");
            if (u.EndsWith("fulfillment_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { fulfillmentPolicies = new[] { new { fulfillmentPolicyId = "fp-1", name = "OmniCard Default" } } }));
            if (u.EndsWith("return_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { returnPolicies = new[] { new { returnPolicyId = "rp-1", name = "OmniCard Default" } } }));
            if (u.EndsWith("payment_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { paymentPolicies = new[] { new { paymentPolicyId = "pp-1", name = "OmniCard Default" } } }));
            // Every PUT (update) reports the no-op error.
            if (req.Method == HttpMethod.Put && u.Contains("_policy/"))
                return (HttpStatusCode.BadRequest, JsonSerializer.Serialize(new
                {
                    errors = new[] { new { errorId = 20403, longMessage = "Business Profile information in the request is the same as in the system" } }
                }));
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        var fulfillment = result.Steps.Single(s => s.Name == "Fulfillment (shipping) policy");
        Assert.Equal(EbaySetupStepStatus.SkippedExisting, fulfillment.Status);
        var payment = result.Steps.Single(s => s.Name == "Payment policy");
        Assert.Equal(EbaySetupStepStatus.SkippedExisting, payment.Status);
        Assert.True(result.Success);
        Assert.Equal("fp-1", settings.Current.FulfillmentPolicyId);
    }

    [Fact]
    public async Task RunSetup_OptIn_AlreadyOptedIn_IsSkippedExisting_NotFailed()
    {
        var settings = WithValidAddress();
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in"))
                return (HttpStatusCode.Conflict, JsonSerializer.Serialize(new { errors = new[] { new { message = "User has already opted in to the program" } } }));
            if (u.Contains("/location/") && req.Method == HttpMethod.Get) return (HttpStatusCode.NotFound, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Post) return (HttpStatusCode.NoContent, "");
            if (u.EndsWith("_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, "{}");
            if (u.Contains("fulfillment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { fulfillmentPolicyId = "fp-1" }));
            if (u.Contains("return_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { returnPolicyId = "rp-1" }));
            if (u.Contains("payment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { paymentPolicyId = "pp-1" }));
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        var optIn = result.Steps.Single(s => s.Name == "Business Policies opt-in");
        Assert.Equal(EbaySetupStepStatus.SkippedExisting, optIn.Status);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task RunSetup_PaymentPolicyFailure_IsNonFatal()
    {
        var settings = WithValidAddress();
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            if (u.Contains("/location/")) return (req.Method == HttpMethod.Get) ? (HttpStatusCode.NotFound, "{}") : (HttpStatusCode.NoContent, "");
            if (u.EndsWith("_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, "{}");
            if (u.EndsWith("fulfillment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { fulfillmentPolicyId = "fp-1" }));
            if (u.EndsWith("return_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { returnPolicyId = "rp-1" }));
            if (u.EndsWith("payment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.BadRequest, JsonSerializer.Serialize(new { errors = new[] { new { message = "not eligible for managed payments" } } }));
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        var pay = result.Steps.Single(s => s.Name == "Payment policy");
        Assert.Equal(EbaySetupStepStatus.Failed, pay.Status);
        Assert.True(result.Success); // location + fulfillment + return succeeded
        Assert.Null(settings.Current.PaymentPolicyId);
    }
}
