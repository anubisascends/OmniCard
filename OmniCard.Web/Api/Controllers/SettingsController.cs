using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Settings;
using OmniCard.Web.Api.Infrastructure;

namespace OmniCard.Web.Api.Controllers;

/// <summary>App/sales settings the SPA can read and edit — the for-sale location that picked cards are
/// moved to, the scan page's value-tier badges, and the receipt-printing configuration (company identity,
/// logo, and thermal-printer layout).</summary>
public sealed class SettingsController(
    ISalesSettingsService settings,
    IScanBadgeSettingsService scanBadges,
    IDataPathService dataPath) : ApiControllerBase
{
    // Logo lives in a dedicated served subdir (kept out of the data-dir root so we never expose
    // sales-settings.json / keys). One canonical basename; the extension follows the uploaded file.
    private const string LogoBaseName = "company-logo";
    private const string BrandingDirName = "branding";
    private static readonly string[] AllowedLogoExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"];
    private const long MaxLogoBytes = 5 * 1024 * 1024;

    private string BrandingDir => Path.Combine(dataPath.DataDirectory, BrandingDirName);

    [HttpGet]
    public ActionResult<SalesSettingsDto> Get() =>
        new SalesSettingsDto(settings.ForSaleLocationId, settings.MovePickedToForSaleLocation);

    [HttpPut]
    public IActionResult Update([FromBody] UpdateSalesSettingsRequest req)
    {
        settings.SetForSaleLocationId(req.ForSaleLocationId);
        settings.SetMovePickedToForSaleLocation(req.MovePickedToForSaleLocation);
        return NoContent();
    }

    /// <summary>The scan page's value-tier badge config (currency + price thresholds). Readable by any
    /// signed-in user — the scan page needs it to render badges.</summary>
    [HttpGet("scan-badges")]
    public ActionResult<ScanBadgeSettingsDto> GetScanBadges()
    {
        var s = scanBadges.Get();
        return new ScanBadgeSettingsDto(s.CurrencyCode, s.Thresholds);
    }

    /// <summary>Update the value-tier badge config. Admin-only — it's an administration setting.</summary>
    [HttpPut("scan-badges")]
    [ApiAuth(RequireAdmin = true)]
    public IActionResult UpdateScanBadges([FromBody] UpdateScanBadgeSettingsRequest req)
    {
        scanBadges.Save(req.CurrencyCode, req.Thresholds);
        return NoContent();
    }

    // --- Receipt printing configuration ---

    /// <summary>The company identity + printer layout used when printing sales receipts. Readable by any
    /// signed-in user (the Sales page needs it to enable the print action); editing is admin-only.</summary>
    [HttpGet("receipt")]
    public ActionResult<ReceiptConfigDto> GetReceiptConfig() =>
        new ReceiptConfigDto(ToDto(settings.GetCompany()), ToDto(settings.GetReceipt()));

    /// <summary>Update the company identity + receipt layout. The logo is managed via the logo endpoints,
    /// so an incoming <c>LogoPath</c> is ignored (the stored logo is preserved).</summary>
    [HttpPut("receipt")]
    [ApiAuth(RequireAdmin = true)]
    public IActionResult UpdateReceiptConfig([FromBody] UpdateReceiptConfigRequest req)
    {
        var c = req.Company;
        var existing = settings.GetCompany();
        settings.SaveCompany(new CompanyProfile
        {
            Name = Trimmed(c.Name),
            AddressLine1 = Trimmed(c.AddressLine1),
            AddressLine2 = Trimmed(c.AddressLine2),
            City = Trimmed(c.City),
            State = Trimmed(c.State),
            PostalCode = Trimmed(c.PostalCode),
            Country = Trimmed(c.Country),
            Email = Trimmed(c.Email),
            Phone = Trimmed(c.Phone),
            LogoPath = existing.LogoPath, // logo is set/cleared only through the logo endpoints
        });

        var r = req.Receipt;
        settings.SaveReceipt(new ReceiptSettings
        {
            WidthMm = Math.Clamp(r.WidthMm, 20, 210),
            MarginMm = Math.Clamp(r.MarginMm, 0, 25),
            FontPointSize = Math.Clamp(r.FontPointSize, 5, 24),
            ShowPrices = r.ShowPrices,
            FooterText = Trimmed(r.FooterText),
            DefaultPrinterName = settings.GetReceipt().DefaultPrinterName,
        });
        return NoContent();
    }

    /// <summary>Upload/replace the receipt logo. Stores it under the served <c>branding/</c> dir and points
    /// the company profile at it.</summary>
    [HttpPost("receipt/logo")]
    [ApiAuth(RequireAdmin = true)]
    public async Task<ActionResult<LogoUploadResultDto>> UploadLogo(IFormFile? file)
    {
        if (file is not { Length: > 0 })
            return BadRequest(new { error = "No file was uploaded." });
        if (file.Length > MaxLogoBytes)
            return BadRequest(new { error = "The logo must be 5 MB or smaller." });
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedLogoExtensions.Contains(ext))
            return BadRequest(new { error = "Unsupported image type. Use PNG, JPG, GIF, WEBP, or BMP." });

        Directory.CreateDirectory(BrandingDir);
        // Drop any prior logo (possibly a different extension) so nothing is orphaned or wrongly served.
        RemoveExistingLogoFiles();
        var fileName = LogoBaseName + ext;
        await using (var stream = System.IO.File.Create(Path.Combine(BrandingDir, fileName)))
            await file.CopyToAsync(stream);

        var company = settings.GetCompany();
        company.LogoPath = $"{BrandingDirName}/{fileName}";
        settings.SaveCompany(company);
        return new LogoUploadResultDto(ToDto(company));
    }

    /// <summary>Remove the receipt logo.</summary>
    [HttpDelete("receipt/logo")]
    [ApiAuth(RequireAdmin = true)]
    public IActionResult DeleteLogo()
    {
        RemoveExistingLogoFiles();
        var company = settings.GetCompany();
        company.LogoPath = null;
        settings.SaveCompany(company);
        return NoContent();
    }

    private void RemoveExistingLogoFiles()
    {
        if (!Directory.Exists(BrandingDir)) return;
        foreach (var f in Directory.EnumerateFiles(BrandingDir, LogoBaseName + ".*"))
        {
            try { System.IO.File.Delete(f); } catch { /* best effort */ }
        }
    }

    private static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private CompanyProfileDto ToDto(CompanyProfile c)
    {
        string? logoUrl = null;
        if (!string.IsNullOrEmpty(c.LogoPath))
        {
            logoUrl = "/" + c.LogoPath.Replace('\\', '/');
            var full = Path.Combine(dataPath.DataDirectory, c.LogoPath);
            // Filename is stable across re-uploads, so version the URL by mtime to defeat browser caching.
            if (System.IO.File.Exists(full))
                logoUrl += "?v=" + new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(full)).ToUnixTimeSeconds();
        }
        return new CompanyProfileDto
        {
            Name = c.Name,
            AddressLine1 = c.AddressLine1,
            AddressLine2 = c.AddressLine2,
            City = c.City,
            State = c.State,
            PostalCode = c.PostalCode,
            Country = c.Country,
            Email = c.Email,
            Phone = c.Phone,
            LogoPath = c.LogoPath,
            LogoUrl = logoUrl,
        };
    }

    private static ReceiptLayoutDto ToDto(ReceiptSettings r) => new()
    {
        WidthMm = r.WidthMm,
        MarginMm = r.MarginMm,
        FontPointSize = r.FontPointSize,
        ShowPrices = r.ShowPrices,
        FooterText = r.FooterText,
    };
}
