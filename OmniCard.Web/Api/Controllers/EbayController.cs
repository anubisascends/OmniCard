using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Ebay;
using OmniCard.Shared.Security;
using OmniCard.Web.Api.Infrastructure;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// Server-side eBay OAuth + seller setup for the SPA. Reuses the desktop's eBay stack
/// (<see cref="IEbayAuthService"/> / <see cref="IEbaySellerSetupService"/>); tokens persist via
/// <c>WebCredentialStore</c>. The connect flow is a normal web redirect: the browser is sent to
/// eBay's consent page and eBay redirects back to <see cref="Callback"/> with an authorization code.
///
/// Deployment: fill the <c>eBay</c> config section (AppId/CertId/RuName/AcceptUrl/Environment) and
/// register this app's callback (<c>/api/ebay/callback</c>) as the RuName accept URL in the eBay dev
/// portal, matching <c>Environment</c> (sandbox vs production).
/// </summary>
[ApiController]
[ApiAuth]
[Route("api/ebay")]
public sealed class EbayController(
    IEbayAuthService auth,
    IEbaySellerSetupService sellerSetup,
    IEbaySellingSettingsService selling,
    ILogger<EbayController> logger) : ControllerBase
{
    [HttpGet("status")]
    [RequirePermission(Permissions.EbayView)]
    public ActionResult<EbayStatusDto> Status()
    {
        var missing = auth.GetMissingConfiguration();
        return new EbayStatusDto(auth.IsConnected, missing.Count == 0, missing);
    }

    /// <summary>Redirects the browser to eBay's OAuth consent page. If the app isn't configured,
    /// bounces back to the settings screen with an error marker instead.</summary>
    [HttpGet("connect")]
    [RequirePermission(Permissions.EbayManage)]
    public IActionResult Connect()
    {
        if (auth.GetMissingConfiguration().Count > 0)
            return Redirect("/app/settings?ebay=misconfigured");
        return Redirect(auth.GetAuthorizationUrl());
    }

    /// <summary>eBay redirects here after consent with an authorization <paramref name="code"/>.
    /// Exchanges it for tokens (stored server-side) and returns the user to the settings screen.</summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? error)
    {
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            logger.LogWarning("eBay OAuth callback returned without a code (error: {Error})", error);
            return Redirect("/app/settings?ebay=failed");
        }

        var ok = await auth.ExchangeCodeForTokensAsync(code);
        return Redirect(ok ? "/app/settings?ebay=connected" : "/app/settings?ebay=failed");
    }

    /// <summary>The editable eBay seller settings (inventory-location address + shipping/return policy
    /// inputs) plus read-only setup results, for the Settings ▸ eBay editor.</summary>
    [HttpGet("selling")]
    [RequirePermission(Permissions.EbayView)]
    public ActionResult<EbaySellingSettingsDto> GetSelling() => ToDto(selling.Get());

    /// <summary>Saves the editable seller settings. Load-then-patch: setup-written results (policy ids,
    /// provisioning state) are preserved, only the user-editable fields are updated. When the seller
    /// is already set up and connected, the saved shipping/return/location values are pushed to eBay
    /// immediately (re-syncing the business policies) so changes like shipping cost take effect on new
    /// listings without a separate "Run eBay Setup" click.</summary>
    [HttpPut("selling")]
    [RequirePermission(Permissions.EbayManage)]
    public async Task<ActionResult<EbaySellingSettingsDto>> SaveSelling([FromBody] EbaySellingSettingsDto dto)
    {
        var s = selling.Get();

        s.LocationName = Clean(dto.LocationName);
        s.AddressLine1 = Clean(dto.AddressLine1);
        s.AddressLine2 = Clean(dto.AddressLine2);
        s.City = Clean(dto.City);
        s.State = Clean(dto.State);
        s.PostalCode = Clean(dto.PostalCode);
        s.Country = Clean(dto.Country)?.ToUpperInvariant();
        s.Phone = Clean(dto.Phone);

        s.FreeShipping = dto.FreeShipping;
        s.ShippingCost = dto.ShippingCost < 0 ? 0 : dto.ShippingCost;
        s.HandlingTimeDays = dto.HandlingTimeDays < 0 ? 0 : dto.HandlingTimeDays;
        if (!string.IsNullOrWhiteSpace(dto.ShippingServiceCode))
            s.ShippingServiceCode = dto.ShippingServiceCode.Trim();

        s.ReturnsAccepted = dto.ReturnsAccepted;
        s.ReturnWindowDays = dto.ReturnWindowDays <= 0 ? 30 : dto.ReturnWindowDays;
        s.ReturnShippingPaidBy = string.Equals(dto.ReturnShippingPaidBy, "Seller", StringComparison.OrdinalIgnoreCase)
            ? ReturnShippingPayer.Seller
            : ReturnShippingPayer.Buyer;

        selling.Save(s);

        // Push the saved values to eBay right away for sellers who have already completed setup, so
        // edits like shipping cost propagate to the eBay business policies (which is where listings
        // read shipping from) instead of silently keeping the stale policy. Best-effort: a save must
        // never fail because of an eBay hiccup — the local settings are already persisted.
        if (auth.IsConnected && selling.IsSetupComplete())
        {
            try
            {
                await sellerSetup.RunSetupAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "eBay policy re-sync after saving selling settings failed");
            }
        }

        return ToDto(selling.Get());
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static EbaySellingSettingsDto ToDto(EbaySellingSettings s) => new()
    {
        LocationName = s.LocationName,
        AddressLine1 = s.AddressLine1,
        AddressLine2 = s.AddressLine2,
        City = s.City,
        State = s.State,
        PostalCode = s.PostalCode,
        Country = s.Country,
        Phone = s.Phone,
        FreeShipping = s.FreeShipping,
        ShippingCost = s.ShippingCost,
        HandlingTimeDays = s.HandlingTimeDays,
        ShippingServiceCode = s.ShippingServiceCode,
        ReturnsAccepted = s.ReturnsAccepted,
        ReturnWindowDays = s.ReturnWindowDays,
        ReturnShippingPaidBy = s.ReturnShippingPaidBy == ReturnShippingPayer.Seller ? "Seller" : "Buyer",
        LocationProvisioned = s.LocationProvisioned,
        FulfillmentPolicyId = s.FulfillmentPolicyId,
        PaymentPolicyId = s.PaymentPolicyId,
        ReturnPolicyId = s.ReturnPolicyId,
        SetupCompletedAt = s.SetupCompletedAt,
    };

    [HttpPost("disconnect")]
    [RequirePermission(Permissions.EbayManage)]
    public IActionResult Disconnect()
    {
        auth.Disconnect();
        return NoContent();
    }

    /// <summary>Runs the idempotent eBay seller setup (opt-in, inventory location, business policies).
    /// Requires an active connection.</summary>
    [HttpPost("setup")]
    [RequirePermission(Permissions.EbayManage)]
    public async Task<ActionResult<EbaySetupResultDto>> Setup()
    {
        if (!auth.IsConnected)
            return BadRequest(new { error = "Connect to eBay first" });

        var result = await sellerSetup.RunSetupAsync();
        var summary = string.Join("; ", result.Steps.Select(s =>
            $"{s.Name}: {s.Status}{(s.Message is null ? "" : $" ({s.Message})")}"));
        return new EbaySetupResultDto(result.Success, summary.Length == 0 ? null : summary);
    }
}
