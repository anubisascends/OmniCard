using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.ImportExport;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Security;
using OmniCard.Web.Api.Infrastructure;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Creates orders from an uploaded CSV using a column-mapping <see cref="OrderImportTemplate"/>.
/// Flow: <c>headers</c> (read the CSV's columns to drive the mapping UI) → <c>preview</c> (parse under a
/// mapping, flag new/matched customers and duplicate orders) → <c>commit</c> (create the selected orders).
/// Custom templates are managed under <c>templates</c>. Orders are header-only (aggregate item count +
/// product value); no inventory lots are touched.</summary>
[ApiController]
[ApiAuth]
[RequirePermission(Permissions.SalesOrdersImport)]
[Route("api/orders/import")]
public sealed class OrderImportController(
    IOrderCsvImportService importer,
    IOrderImportTemplateService templates) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The logical fields a CSV column can be mapped to (enum names), for the mapping UI.</summary>
    [HttpGet("fields")]
    public ActionResult<IEnumerable<string>> Fields() =>
        Ok(Enum.GetNames<OrderImportField>());

    // --- Templates ---

    [HttpGet("templates")]
    public ActionResult<IEnumerable<OrderImportTemplateDto>> GetTemplates() =>
        Ok(templates.GetAll().Select(ToDto));

    [HttpPost("templates")]
    public ActionResult<OrderImportTemplateDto> SaveTemplate([FromBody] SaveOrderImportTemplateRequest req)
    {
        try
        {
            var saved = templates.Save(new OrderImportTemplate
            {
                Id = req.Id ?? "",
                Name = req.Name,
                Channel = ParseChannel(req.Channel),
                ColumnMappings = new Dictionary<string, string>(req.ColumnMappings, StringComparer.Ordinal),
            });
            return Ok(ToDto(saved));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("templates/{id}")]
    public IActionResult DeleteTemplate(string id) =>
        templates.Delete(id) ? NoContent() : NotFound();

    // --- Import ---

    /// <summary>The raw column headers of an uploaded CSV (in file order).</summary>
    [HttpPost("headers")]
    public IActionResult Headers(IFormFile file)
        => WithTempFile(file, path => Ok(new { headers = importer.ReadHeaders(path) }));

    /// <summary>Parse the CSV under an inline column mapping and return the resolved preview rows.</summary>
    [HttpPost("preview")]
    public IActionResult Preview(
        IFormFile file,
        [FromForm] string mappingsJson,
        [FromForm] string channel = "Manual")
    {
        Dictionary<string, string>? mappings;
        try
        {
            mappings = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingsJson ?? "{}", JsonOptions);
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Invalid column mapping." });
        }

        var template = new OrderImportTemplate
        {
            Channel = ParseChannel(channel),
            ColumnMappings = new Dictionary<string, string>(mappings ?? [], StringComparer.Ordinal),
        };

        return WithTempFile(file, path =>
        {
            var preview = importer.PreviewImport(path, template);
            return Ok(new OrderImportPreviewDto([.. preview.Rows.Select(ToDto)], preview.Warnings));
        });
    }

    /// <summary>Create orders for the selected preview rows. Idempotent: existing order numbers are skipped.</summary>
    [HttpPost("commit")]
    public ActionResult<object> Commit([FromBody] CommitOrderImportRequest req)
    {
        var preview = new OrderImportPreview { Rows = [.. req.Rows.Select(FromDto)] };
        var created = importer.Commit(preview, ParseChannel(req.Channel));
        return Ok(new { created });
    }

    // --- Helpers ---

    private IActionResult WithTempFile(IFormFile file, Func<string, IActionResult> work)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        var path = Path.Combine(Path.GetTempPath(), $"omnicard-orderimport-{Guid.NewGuid():N}.csv");
        try
        {
            using (var fs = System.IO.File.Create(path))
                file.CopyTo(fs);
            return work(path);
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }

    private static SalesChannel ParseChannel(string? channel) =>
        Enum.TryParse<SalesChannel>(channel, ignoreCase: true, out var c) ? c : SalesChannel.Manual;

    private static OrderImportTemplateDto ToDto(OrderImportTemplate t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        IsBuiltIn = t.IsBuiltIn,
        Channel = t.Channel.ToString(),
        ColumnMappings = new Dictionary<string, string>(t.ColumnMappings),
    };

    private static OrderImportRowDto ToDto(OrderImportRow r) => new()
    {
        OrderNumber = r.OrderNumber,
        CustomerName = r.CustomerName,
        AddressLine1 = r.AddressLine1,
        AddressLine2 = r.AddressLine2,
        City = r.City,
        State = r.State,
        PostalCode = r.PostalCode,
        Country = r.Country,
        OrderDate = r.OrderDate,
        ShippingFeePaid = r.ShippingFeePaid,
        ItemCount = r.ItemCount,
        ValueOfProducts = r.ValueOfProducts,
        TrackingNumber = r.TrackingNumber,
        Carrier = r.Carrier,
        MatchedCustomerId = r.MatchedCustomerId,
        IsNewCustomer = r.IsNewCustomer,
        IsDuplicateOrder = r.IsDuplicateOrder,
        Include = r.Include,
        CanInclude = r.CanInclude,
        StatusText = r.StatusText,
    };

    private static OrderImportRow FromDto(OrderImportRowDto d) => new()
    {
        OrderNumber = d.OrderNumber,
        CustomerName = d.CustomerName,
        AddressLine1 = d.AddressLine1,
        AddressLine2 = d.AddressLine2,
        City = d.City,
        State = d.State,
        PostalCode = d.PostalCode,
        Country = d.Country,
        OrderDate = d.OrderDate,
        ShippingFeePaid = d.ShippingFeePaid,
        ItemCount = d.ItemCount,
        ValueOfProducts = d.ValueOfProducts,
        TrackingNumber = d.TrackingNumber,
        Carrier = d.Carrier,
        MatchedCustomerId = d.MatchedCustomerId,
        IsNewCustomer = d.IsNewCustomer,
        IsDuplicateOrder = d.IsDuplicateOrder,
        Include = d.Include,
    };
}
