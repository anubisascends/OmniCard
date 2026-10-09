using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Audit;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Security;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Api.Mapping;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Orders board: the customizable kanban lanes, the orders, status/lane changes, and full
/// order CRUD (create, edit header, add/remove line items, delete). Backed by
/// <see cref="IOrderService"/>; header edits load-patch through the DB factory so the SQL Server
/// <c>RowVersion</c> concurrency token is honored.</summary>
public sealed class OrdersController(
    IOrderService orders,
    ICustomerService customers,
    ISalesSettingsService settings,
    IReceiptService receipts,
    IReceiptPdfExporter receiptPdf,
    IDbContextFactory<OmniCardDbContext> dbFactory) : ApiControllerBase
{
    /// <summary>Downloadable PDF receipt for an order.</summary>
    [HttpGet("{id:int}/receipt.pdf")]
    [RequirePermission(Permissions.SalesOrdersView)]
    public IActionResult Receipt(int id)
    {
        var doc = receipts.BuildReceipt(id);
        var bytes = TempFile.Produce(".pdf", p => receiptPdf.Export(doc, p));
        return File(bytes, "application/pdf", $"receipt-{id}.pdf");
    }

    /// <summary>Print-ready HTML receipt sized to the configured roll width (<c>@page size: {width}mm
    /// auto</c>) and auto-opening the browser print dialog. Preferred for thermal printers — it avoids the
    /// PDF path's tendency to pad the job out to a full Letter/A4 sheet.</summary>
    [HttpGet("{id:int}/receipt.html")]
    [RequirePermission(Permissions.SalesOrdersView)]
    public ContentResult ReceiptHtml(int id)
    {
        var doc = receipts.BuildReceipt(id);
        return Content(ReceiptHtmlRenderer.Render(doc), "text/html");
    }

    /// <summary>The kanban lanes in board order (customizable; falls back to built-in defaults).</summary>
    [HttpGet("lanes")]
    [RequirePermission(Permissions.SalesOrdersView)]
    public ActionResult<IReadOnlyList<WorkflowLaneDto>> Lanes() =>
        settings.GetWorkflowLanes().Select(DtoMapping.ToDto).ToList();

    /// <summary>All orders, newest first, each with its customer name + line count/total for the
    /// kanban cards.</summary>
    [HttpGet]
    [RequirePermission(Permissions.SalesOrdersView)]
    public ActionResult<IReadOnlyList<OrderDto>> Get()
    {
        var list = orders.GetOrders();
        return Decorate(list).Select(DtoMapping.ToDto).ToList();
    }

    /// <summary>Hydrates the display-only customer name + line count/total for card/result display.</summary>
    private List<Order> Decorate(List<Order> list)
    {
        var summaries = orders.GetOrderLineSummaries().ToDictionary(s => s.OrderId);
        var customerNames = customers.GetAll().ToDictionary(c => c.Id, c => c.Name);
        foreach (var o in list)
        {
            o.CustomerNameDisplay = customerNames.GetValueOrDefault(o.CustomerId);
            if (summaries.TryGetValue(o.Id, out var s))
            {
                o.LineItemCount = s.ItemCount;
                o.LineTotal = s.Total;
            }
        }
        return list;
    }

    /// <summary>Move an order to a new status/lane (kanban drag). On a transition to Shipped this
    /// records the sale + marks listings sold (eBay auto-end is stubbed until Phase 5).</summary>
    [HttpPut("{id:int}/status")]
    [RequirePermission(Permissions.SalesOrdersEdit)]
    public async Task<IActionResult> SetStatus(int id, [FromBody] SetOrderStatusRequest req)
    {
        if (!Enum.TryParse<OrderStatus>(req.Status, ignoreCase: true, out var status))
            return BadRequest(new { error = $"Invalid status '{req.Status}'." });
        await orders.SetStatusAsync(id, status, req.StageKey);
        return NoContent();
    }

    // --- Ship by scanning the label ---

    /// <summary>Looks up the order(s) whose tracking number matches a scanned label barcode and, when
    /// <see cref="ShipScanRequest.Ship"/> is set and exactly one open (Created/Packed) order matches,
    /// ships it — the Ship page's auto-ship path. Shipping goes through the same
    /// <see cref="IOrderService.SetStatusAsync"/> as a kanban drag into the Shipped lane.</summary>
    [HttpPost("ship-scan")]
    [RequirePermission(Permissions.SalesOrdersShip, Permissions.SalesOrdersEdit)]
    public async Task<ActionResult<ShipScanResultDto>> ShipScan([FromBody] ShipScanRequest req)
    {
        var tracking = TrackingNumbers.Extract(req.Code);
        if (tracking.Length == 0)
            return BadRequest(new { error = "Scan or enter a tracking number." });

        var matches = orders.FindByTracking(req.Code);
        var open = matches.Where(IsOpen).ToList();

        if (open.Count == 1)
        {
            if (!req.Ship)
                return new ShipScanResultDto("ready", tracking, Decorate(open).Select(DtoMapping.ToDto).ToList());
            await ShipAsync(open[0].Id);
            var shipped = orders.GetOrder(open[0].Id)!;
            return new ShipScanResultDto("shipped", tracking, Decorate([shipped]).Select(DtoMapping.ToDto).ToList());
        }
        if (open.Count > 1)
            return new ShipScanResultDto("ambiguous", tracking, Decorate(open).Select(DtoMapping.ToDto).ToList());
        if (matches.Count > 0)
            return new ShipScanResultDto("alreadyShipped", tracking, Decorate(matches).Select(DtoMapping.ToDto).ToList());
        return new ShipScanResultDto("notFound", tracking, []);
    }

    /// <summary>Ships one open order into the first Shipped-behavior lane (the Ship page's confirm /
    /// pick-from-several path). 409 when the order isn't Created/Packed.</summary>
    [HttpPost("{id:int}/ship")]
    [RequirePermission(Permissions.SalesOrdersShip, Permissions.SalesOrdersEdit)]
    public async Task<ActionResult<OrderDto>> Ship(int id)
    {
        var order = orders.GetOrder(id);
        if (order is null)
            return NotFound();
        if (!IsOpen(order))
            return Conflict(new { error = $"Order is already {order.Status.ToString().ToLowerInvariant()}." });

        await ShipAsync(id);
        return DtoMapping.ToDto(Decorate([orders.GetOrder(id)!])[0]);
    }

    private static bool IsOpen(Order o) => o.Status is OrderStatus.Created or OrderStatus.Packed;

    private Task ShipAsync(int orderId)
    {
        var lane = settings.GetWorkflowLanes().FirstOrDefault(l => l.Behavior == OrderStatus.Shipped);
        return orders.SetStatusAsync(orderId, OrderStatus.Shipped, lane?.Key);
    }

    // --- CRUD ---

    /// <summary>An order's header + line items for the detail/edit view.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.SalesOrdersView)]
    public ActionResult<OrderDetailDto> GetOne(int id)
    {
        var order = orders.GetOrder(id);
        if (order is null)
            return NotFound();

        order.CustomerNameDisplay = customers.Get(order.CustomerId)?.Name;
        var lines = orders.GetLines(id);
        order.LineItemCount = lines.Sum(l => l.Quantity);
        order.LineTotal = lines.Sum(l => l.UnitSalePrice * l.Quantity);

        return new OrderDetailDto(DtoMapping.ToDto(order), lines.Select(ToLineDto).ToList());
    }

    [HttpPost]
    [RequirePermission(Permissions.SalesOrdersCreate)]
    public ActionResult<OrderDto> Create([FromBody] CreateOrderRequest req)
    {
        if (req.CustomerId <= 0 || customers.Get(req.CustomerId) is null)
            return BadRequest(new { error = "A valid customer is required" });
        if (!Enum.TryParse<SalesChannel>(req.Channel, ignoreCase: true, out var channel))
            return BadRequest(new { error = $"Invalid channel '{req.Channel}'" });

        var created = orders.CreateOrder(req.CustomerId, channel, string.IsNullOrWhiteSpace(req.OrderNumber) ? null : req.OrderNumber);
        created.CustomerNameDisplay = customers.Get(created.CustomerId)?.Name;
        return CreatedAtAction(nameof(GetOne), new { id = created.Id }, DtoMapping.ToDto(created));
    }

    /// <summary>Edit a pre-ship order's header. Load-then-patch for the rowversion token.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(Permissions.SalesOrdersEdit)]
    public IActionResult Update(int id, [FromBody] UpdateOrderRequest req)
    {
        if (!Enum.TryParse<SalesChannel>(req.Channel, ignoreCase: true, out var channel))
            return BadRequest(new { error = $"Invalid channel '{req.Channel}'" });

        using var ctx = dbFactory.CreateDbContext();
        var order = ctx.Orders.FirstOrDefault(o => o.Id == id);
        if (order is null)
            return NotFound();

        order.Channel = channel;
        order.OrderNumber = string.IsNullOrWhiteSpace(req.OrderNumber) ? null : req.OrderNumber;
        order.TrackingNumber = req.TrackingNumber;
        order.Carrier = req.Carrier;
        order.ShippingChargedToBuyer = req.ShippingChargedToBuyer;
        order.ShippingCost = req.ShippingCost;
        order.MarketplaceFees = req.MarketplaceFees;
        order.Notes = req.Notes;
        ctx.SaveChanges();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.SalesOrdersDelete)]
    public IActionResult Delete(int id)
    {
        try
        {
            orders.DeleteOrder(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            // DeleteOrder throws on a Shipped/Completed order (its sale is already recorded).
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:int}/lines")]
    [RequirePermission(Permissions.SalesOrdersEdit)]
    public ActionResult<OrderLineDto> AddLine(int id, [FromBody] AddOrderLineRequest req)
    {
        if (orders.GetOrder(id) is null)
            return NotFound();
        if (req.LotId <= 0)
            return BadRequest(new { error = "A card (lot) is required" });

        var line = orders.AddLine(id, req.LotId, req.UnitSalePrice);
        return ToLineDto(line);
    }

    [HttpDelete("lines/{lineId:int}")]
    [RequirePermission(Permissions.SalesOrdersEdit)]
    public IActionResult RemoveLine(int lineId)
    {
        orders.RemoveLine(lineId);
        return NoContent();
    }

    private static OrderLineDto ToLineDto(OrderLine l) => new(
        l.Id, l.LotId, l.NameSnapshot, l.SetSnapshot, l.ConditionSnapshot, l.IsFoilSnapshot,
        l.Quantity, l.UnitSalePrice);
}
