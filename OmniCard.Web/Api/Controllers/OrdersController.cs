using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Audit;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Security;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Api.Mapping;
using OmniCard.Web.Services;
using OmniCard.Web.Services.Kiosk;

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
    IDbContextFactory<OmniCardDbContext> dbFactory,
    KioskService? kiosk = null) : ApiControllerBase
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

        // Orders sell listed cards only — and never more copies than the listing has free.
        using (var ctx = dbFactory.CreateDbContext())
        {
            var listed = ActiveListings(ctx)
                .Where(x => x.Lot.Id == req.LotId)
                .Select(x => new { x.Lot.Quantity, ListedQuantity = x.Listing.Quantity })
                .FirstOrDefault();
            if (listed is null)
                return BadRequest(new { error = "Only cards listed for sale can be added to an order." });
            var committed = ReservedCopies(ctx).GetValueOrDefault(req.LotId);
            if (Math.Min(listed.Quantity, listed.ListedQuantity) - committed <= 0)
                return Conflict(new { error = "Every listed copy of this card is already on an open order." });
        }

        var line = orders.AddLine(id, req.LotId, req.UnitSalePrice);
        return ToLineDto(line);
    }

    /// <summary>The order editor's add-card search: listed-for-sale cards whose name, set code or set
    /// name contains every word of <paramref name="q"/> (or whose collector number is one of them),
    /// with copies not already on an open order. The rest of the collection never shows up here.
    /// <paramref name="game"/> is ignored when the store-wide "search all games" sales setting is on.</summary>
    [HttpGet("sellable")]
    [RequirePermission(Permissions.SalesOrdersEdit)]
    public ActionResult<IReadOnlyList<SellableListingDto>> Sellable(
        [FromQuery] string? q, [FromQuery] string? game, [FromQuery] int take = 20)
    {
        var gameFilter = settings.OrderSearchAllGames ? null : LocationsController.ParseGame(game);
        using var ctx = dbFactory.CreateDbContext();
        var query = ActiveListings(ctx).Where(x => gameFilter == null || x.Product.Game == gameFilter);
        foreach (var term in (q ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pattern = $"%{EscapeLike(term)}%";
            query = query.Where(x => EF.Functions.Like(x.Product.Name, pattern, "\\")
                || EF.Functions.Like(x.Product.SetCode ?? "", pattern, "\\")
                || EF.Functions.Like(x.Product.SetName ?? "", pattern, "\\")
                || x.Product.CollectorNumber == term);
        }

        var committed = ReservedCopies(ctx);
        return query
            .OrderBy(x => x.Product.Name).ThenBy(x => x.Product.SetCode)
            .ToList()
            .Select(x => new SellableListingDto(
                x.Lot.Id, x.Product.Name, x.Product.SetName ?? "", x.Product.SetCode ?? "", x.Product.CollectorNumber,
                x.Lot.Condition, x.Product.Foil, x.Listing.ListedPrice, x.Listing.Status.ToString(),
                Math.Min(x.Lot.Quantity, x.Listing.Quantity) - committed.GetValueOrDefault(x.Lot.Id)))
            .Where(s => s.Available > 0)
            .Take(Math.Clamp(take, 1, 100))
            .ToList();
    }

    private sealed class ListedLot
    {
        public required Listing Listing { get; init; }
        public required InventoryLot Lot { get; init; }
        public required Product Product { get; init; }
    }

    /// <summary>Listed/Picked listings joined to their lot and product.</summary>
    private static IQueryable<ListedLot> ActiveListings(OmniCardDbContext ctx) =>
        from listing in ctx.Listings.AsNoTracking()
        where listing.Status == ListingStatus.Listed || listing.Status == ListingStatus.Picked
        join lot in ctx.Lots.AsNoTracking() on listing.LotId equals lot.Id
        join product in ctx.Products.AsNoTracking() on lot.ProductId equals product.Id
        select new ListedLot { Listing = listing, Lot = lot, Product = product };

    /// <summary>Copies per lot already on an open (Created/Packed) order — they leave inventory when that
    /// order ships, so they can't be sold again.</summary>
    private static Dictionary<int, int> CommittedCopies(OmniCardDbContext ctx) =>
        (from line in ctx.OrderLines.AsNoTracking()
         join order in ctx.Orders.AsNoTracking() on line.OrderId equals order.Id
         where line.LotId != null && (order.Status == OrderStatus.Created || order.Status == OrderStatus.Packed)
         group line by line.LotId!.Value into g
         select new { LotId = g.Key, Quantity = g.Sum(l => l.Quantity) })
        .ToDictionary(x => x.LotId, x => x.Quantity);

    /// <summary>Copies per lot that can't go on another order: those on open orders plus those a customer
    /// holds at the kiosk.</summary>
    private Dictionary<int, int> ReservedCopies(OmniCardDbContext ctx)
    {
        var reserved = CommittedCopies(ctx);
        if (kiosk is not null)
            foreach (var (lotId, qty) in kiosk.HeldCopies(ctx))
                reserved[lotId] = reserved.GetValueOrDefault(lotId) + qty;
        return reserved;
    }

    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

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
