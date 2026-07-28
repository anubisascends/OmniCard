using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.eBay;

public class EbaySellerSetupService : IEbaySellerSetupService
{
    private const string Marketplace = "EBAY_US";
    private const string PolicyName = "OmniCard Default";

    private readonly EbaySettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEbayAuthService _auth;
    private readonly IEbaySellingSettingsService _sellingSettings;
    private readonly ILogger<EbaySellerSetupService> _logger;

    public EbaySellerSetupService(
        IOptions<EbaySettings> settings,
        IHttpClientFactory httpClientFactory,
        IEbayAuthService auth,
        IEbaySellingSettingsService sellingSettings,
        ILogger<EbaySellerSetupService> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _auth = auth;
        _sellingSettings = sellingSettings;
        _logger = logger;
    }

    public async Task<EbaySetupResult> RunSetupAsync(IProgress<string>? progress = null)
    {
        var result = new EbaySetupResult();
        var token = await _auth.GetAccessTokenAsync();
        if (token is null)
        {
            result.Steps.Add(new EbaySetupStep("Authorization", EbaySetupStepStatus.Failed, "Not connected to eBay."));
            return result;
        }

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var s = _sellingSettings.Get();

        progress?.Report("Opting into Business Policies…");
        result.Steps.Add(await OptInAsync(client));

        progress?.Report("Ensuring inventory location…");
        result.Steps.Add(await EnsureLocationAsync(client, s));

        // Policy steps are added by Task 3.
        await FinalizeAsync(client, s, result, progress);

        _sellingSettings.Save(s);
        return result;
    }

    // Task 3 replaces this stub with real policy steps + Success calculation.
    protected virtual Task FinalizeAsync(HttpClient client, EbaySellingSettings s, EbaySetupResult result, IProgress<string>? progress)
        => Task.CompletedTask;

    private async Task<EbaySetupStep> OptInAsync(HttpClient client)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { programType = "SELLING_POLICY_MANAGEMENT" });
            var resp = await client.PostAsync($"{_settings.ApiBaseUrl}/sell/account/v1/program/opt_in",
                new StringContent(body, Encoding.UTF8, "application/json"));
            if (resp.IsSuccessStatusCode)
                return new EbaySetupStep("Business Policies opt-in", EbaySetupStepStatus.Ok, null);

            var err = await resp.Content.ReadAsStringAsync();
            // Already opted in is reported as an error by eBay; treat as existing.
            if (err.Contains("already opted in", StringComparison.OrdinalIgnoreCase))
                return new EbaySetupStep("Business Policies opt-in", EbaySetupStepStatus.SkippedExisting, null);

            _logger.LogWarning("Opt-in failed: {Status} — {Error}", resp.StatusCode, err);
            return new EbaySetupStep("Business Policies opt-in", EbaySetupStepStatus.Failed, $"{resp.StatusCode}: {err}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Opt-in threw");
            return new EbaySetupStep("Business Policies opt-in", EbaySetupStepStatus.Failed, ex.Message);
        }
    }

    private async Task<EbaySetupStep> EnsureLocationAsync(HttpClient client, EbaySellingSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.Country) || string.IsNullOrWhiteSpace(s.PostalCode)
            || string.IsNullOrWhiteSpace(s.AddressLine1) || string.IsNullOrWhiteSpace(s.City))
        {
            return new EbaySetupStep("Inventory location", EbaySetupStepStatus.Failed,
                "Address is incomplete — set Address, City, Postal code and Country (ISO-2, e.g. US) in Settings ▸ eBay Selling.");
        }

        var key = Uri.EscapeDataString(s.MerchantLocationKey);
        var url = $"{_settings.ApiBaseUrl}/sell/inventory/v1/location/{key}";
        try
        {
            var existing = await client.GetAsync(url);
            if (existing.IsSuccessStatusCode)
            {
                s.LocationProvisioned = true;
                return new EbaySetupStep("Inventory location", EbaySetupStepStatus.SkippedExisting, null);
            }

            // Only a 404 means "location does not exist yet, create it". Any other
            // non-success (401 expired/insufficient-scope, 5xx, rate-limiting) is a real
            // error and must NOT fall through to a create that would mask it.
            if (existing.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                var getErr = await existing.Content.ReadAsStringAsync();
                _logger.LogWarning("Get location failed: {Status} — {Error}", existing.StatusCode, getErr);
                return new EbaySetupStep("Inventory location", EbaySetupStepStatus.Failed, $"{existing.StatusCode}: {getErr}");
            }

            var payload = new
            {
                location = new
                {
                    address = new
                    {
                        addressLine1 = s.AddressLine1,
                        addressLine2 = s.AddressLine2,
                        city = s.City,
                        stateOrProvince = s.State,
                        postalCode = s.PostalCode,
                        country = s.Country,
                    }
                },
                name = string.IsNullOrWhiteSpace(s.LocationName) ? "OmniCard Primary" : s.LocationName,
                merchantLocationStatus = "ENABLED",
                locationTypes = new[] { "WAREHOUSE" },
                phone = s.Phone,
            };
            var resp = await client.PostAsync(url,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            if (resp.IsSuccessStatusCode)
            {
                s.LocationProvisioned = true;
                return new EbaySetupStep("Inventory location", EbaySetupStepStatus.Ok, null);
            }

            var err = await resp.Content.ReadAsStringAsync();
            _logger.LogWarning("Create location failed: {Status} — {Error}", resp.StatusCode, err);
            return new EbaySetupStep("Inventory location", EbaySetupStepStatus.Failed, $"{resp.StatusCode}: {err}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ensure location threw");
            return new EbaySetupStep("Inventory location", EbaySetupStepStatus.Failed, ex.Message);
        }
    }
}
