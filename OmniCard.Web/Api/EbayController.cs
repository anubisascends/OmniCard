using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Interfaces;

namespace OmniCard.Web.Api;

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
    ILogger<EbayController> logger) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<EbayStatusDto> Status()
    {
        var missing = auth.GetMissingConfiguration();
        return new EbayStatusDto(auth.IsConnected, missing.Count == 0, missing);
    }

    /// <summary>Redirects the browser to eBay's OAuth consent page. If the app isn't configured,
    /// bounces back to the settings screen with an error marker instead.</summary>
    [HttpGet("connect")]
    public IActionResult Connect()
    {
        if (auth.GetMissingConfiguration().Count > 0)
            return Redirect("/app/settings?ebay=misconfigured");
        return Redirect(auth.GetAuthorizationUrl());
    }

    /// <summary>eBay redirects here after consent with an authorization <paramref name="code"/>.
    /// Exchanges it for tokens (stored server-side) and returns the user to the settings screen.</summary>
    [HttpGet("callback")]
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

    [HttpPost("disconnect")]
    public IActionResult Disconnect()
    {
        auth.Disconnect();
        return NoContent();
    }

    /// <summary>Runs the idempotent eBay seller setup (opt-in, inventory location, business policies).
    /// Requires an active connection.</summary>
    [HttpPost("setup")]
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
