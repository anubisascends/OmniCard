using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class OrderService(
    IDbContextFactory<OmniCardDbContext> dbContextFactory,
    IListingService listingService,
    IEbayListingService ebayListingService,
    ILogger<OrderService> logger) : IOrderService
{
    public List<Order> GetOrders()
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Orders.AsNoTracking().OrderByDescending(o => o.OrderDate).ToList();
    }

    public Order? GetOrder(int id)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Orders.AsNoTracking().FirstOrDefault(o => o.Id == id);
    }

    public List<OrderLine> GetLines(int orderId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.OrderLines.AsNoTracking().Where(l => l.OrderId == orderId).ToList();
    }

    public List<OrderLineSummary> GetOrderLineSummaries()
    {
        using var ctx = dbContextFactory.CreateDbContext();
        // Aggregate client-side: SQLite stores decimal as TEXT, so SUM(decimal) can't be
        // translated server-side. Volumes (order lines) are small.
        return ctx.OrderLines.AsNoTracking()
            .Select(l => new { l.OrderId, l.Quantity, l.UnitSalePrice })
            .AsEnumerable()
            .GroupBy(l => l.OrderId)
            .Select(g => new OrderLineSummary(g.Key, g.Sum(l => l.Quantity), g.Sum(l => l.Quantity * l.UnitSalePrice)))
            .ToList();
    }

    public Order CreateOrder(int customerId, SalesChannel channel, string? orderNumber)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var order = new Order
        {
            CustomerId = customerId,
            Channel = channel,
            OrderNumber = orderNumber,
            Status = OrderStatus.Created,
            StageKey = OrderStatus.Created.ToString().ToLowerInvariant(),
            OrderDate = DateTime.UtcNow,
        };
        ctx.Orders.Add(order);
        ctx.SaveChanges();
        return order;
    }

    public void UpdateOrder(Order order)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.Orders.Update(order);
        ctx.SaveChanges();
    }

    public OrderLine AddLine(int orderId, int lotId, decimal unitSalePrice)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var lot = ctx.Lots.AsNoTracking().FirstOrDefault(l => l.Id == lotId)
                  ?? throw new InvalidOperationException($"Lot {lotId} not found.");
        var product = ctx.Products.AsNoTracking().FirstOrDefault(p => p.Id == lot.ProductId);

        var line = new OrderLine
        {
            OrderId = orderId,
            LotId = lotId,
            ProductId = lot.ProductId,
            NameSnapshot = product?.Name ?? "",
            SetSnapshot = product?.SetName,
            ConditionSnapshot = lot.Condition,
            IsFoilSnapshot = product?.Foil ?? false,
            Quantity = 1,
            UnitSalePrice = unitSalePrice,
        };
        ctx.OrderLines.Add(line);
        ctx.SaveChanges();
        return line;
    }

    public List<OrderLine> AddLines(int orderId, IEnumerable<int> lotIds, decimal unitSalePrice)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var ids = lotIds.Distinct().ToList();
        var lots = ctx.Lots.AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionary(l => l.Id);
        var productIds = lots.Values.Select(l => l.ProductId).Distinct().ToList();
        var products = ctx.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionary(p => p.Id);

        var lines = new List<OrderLine>();
        foreach (var lotId in ids)
        {
            if (!lots.TryGetValue(lotId, out var lot))
                throw new InvalidOperationException($"Lot {lotId} not found.");
            var product = products.GetValueOrDefault(lot.ProductId);

            lines.Add(new OrderLine
            {
                OrderId = orderId,
                LotId = lotId,
                ProductId = lot.ProductId,
                NameSnapshot = product?.Name ?? "",
                SetSnapshot = product?.SetName,
                ConditionSnapshot = lot.Condition,
                IsFoilSnapshot = product?.Foil ?? false,
                Quantity = 1,
                UnitSalePrice = unitSalePrice,
            });
        }

        ctx.OrderLines.AddRange(lines);
        ctx.SaveChanges();
        return lines;
    }

    public void RemoveLine(int orderLineId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var line = ctx.OrderLines.FirstOrDefault(l => l.Id == orderLineId);
        if (line is null) return;
        ctx.OrderLines.Remove(line);
        ctx.SaveChanges();
    }

    public async Task SetStatusAsync(int orderId, OrderStatus status, string? stageKey = null)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var order = ctx.Orders.FirstOrDefault(o => o.Id == orderId);
        if (order is null) return;

        var shipping = status == OrderStatus.Shipped && order.Status != OrderStatus.Shipped
                       && order.Status != OrderStatus.Completed && order.Status != OrderStatus.Cancelled;

        order.Status = status;
        order.StageKey = stageKey ?? status.ToString().ToLowerInvariant();

        if (!shipping)
        {
            await ctx.SaveChangesAsync();
            return;
        }

        order.ShippedAt = DateTime.UtcNow;
        var lines = ctx.OrderLines.Where(l => l.OrderId == orderId).ToList();
        var lotIds = lines.Where(l => l.LotId is not null).Select(l => l.LotId!.Value).ToList();

        // Capture active eBay listings BEFORE the lots are removed — removing a lot
        // cascade-deletes its EbayListing row, so we must grab them first.
        var ebayToEnd = ctx.EbayListings.AsNoTracking()
            .Where(e => lotIds.Contains(e.LotId) && e.Status == EbayListingStatus.Active)
            .ToList();

        foreach (var line in lines)
        {
            if (line.LotId is not int lotId) continue;
            var lot = ctx.Lots.FirstOrDefault(l => l.Id == lotId);
            if (lot is null) continue;

            // Record the sale first (scalar values survive the lot removal below).
            ctx.Movements.Add(new InventoryMovement
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                OrderLineId = line.Id,
                Type = MovementType.Sell,
                Quantity = line.Quantity,
                UnitValue = line.UnitSalePrice,
                Timestamp = DateTime.UtcNow,
                Note = order.OrderNumber ?? $"Order {order.Id}",
            });

            lot.Quantity -= line.Quantity;
            if (lot.Quantity <= 0)
                ctx.Lots.Remove(lot);
        }
        await ctx.SaveChangesAsync();

        // Mark each sold lot's active listing Sold (separate context inside the service).
        foreach (var line in lines.Where(l => l.LotId is not null))
            listingService.MarkSold(line.LotId!.Value, line.Id);

        // Auto-end any active eBay listing for the sold lots so it can't double-sell.
        // Best-effort: a failure never blocks the sale — it's logged with the item id so
        // the still-live listing can be ended manually on eBay.
        foreach (var listing in ebayToEnd)
        {
            try
            {
                var ended = await ebayListingService.EndListingAsync(listing);
                if (!ended)
                    logger.LogError("Auto-end of eBay listing {ItemId} (lot {LotId}) after sale FAILED — end it manually on eBay.", listing.EbayItemId, listing.LotId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-end of eBay listing {ItemId} (lot {LotId}) after sale threw — end it manually on eBay.", listing.EbayItemId, listing.LotId);
            }
        }
    }

    public void DeleteOrder(int orderId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var order = ctx.Orders.FirstOrDefault(o => o.Id == orderId);
        if (order is null) return;
        if (order.Status is OrderStatus.Shipped or OrderStatus.Completed)
            throw new InvalidOperationException(
                $"Can't delete a {order.Status} order (its sale is recorded and inventory removed).");

        var lines = ctx.OrderLines.Where(l => l.OrderId == orderId).ToList();
        ctx.OrderLines.RemoveRange(lines);
        ctx.Orders.Remove(order);
        ctx.SaveChanges();
    }

    public async Task EditCompletedOrder(int orderId, Order updatedHeader, List<OrderLine> updatedLines, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required to edit a completed order.", nameof(reason));
        reason = reason.Trim();

        // NOTE: intentionally NOT AsNoTracking() here (unlike every read-only method above) —
        // this method mutates Order/OrderLine/InventoryMovement/InventoryLot entities directly.
        using var ctx = dbContextFactory.CreateDbContext();
        var order = ctx.Orders.FirstOrDefault(o => o.Id == orderId)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");
        if (order.Status != OrderStatus.Completed)
            throw new InvalidOperationException("Only a Completed order can be edited this way.");

        var currentLines = ctx.OrderLines.Where(l => l.OrderId == orderId).ToDictionary(l => l.Id);
        var updatedById = updatedLines.Where(l => l.Id != 0).ToDictionary(l => l.Id);
        var addedLines = updatedLines.Where(l => l.Id == 0).ToList();

        // Classify: a line missing from the updated set, or present with Quantity <= 0, is a removal.
        var toRemoveIds = currentLines.Keys
            .Where(id => !updatedById.TryGetValue(id, out var u) || u.Quantity <= 0)
            .ToList();
        var toChangeIds = currentLines.Keys
            .Where(id => updatedById.TryGetValue(id, out var u) && u.Quantity > 0
                && (u.Quantity != currentLines[id].Quantity || u.UnitSalePrice != currentLines[id].UnitSalePrice))
            .ToList();

        // ---- Validation pass — no writes yet, so any throw here leaves the DB untouched. ----
        foreach (var id in toChangeIds)
        {
            if (updatedById[id].Quantity > currentLines[id].Quantity)
                throw new InvalidOperationException(
                    $"Line '{currentLines[id].NameSnapshot}': quantity can't be increased on an existing line — add a new line instead.");
        }

        var linkedMovements = new Dictionary<int, InventoryMovement>();
        foreach (var id in toRemoveIds.Concat(toChangeIds).Distinct())
        {
            var movement = ctx.Movements.FirstOrDefault(m => m.OrderLineId == id && m.Type == MovementType.Sell);
            if (movement is null)
                throw new InvalidOperationException(
                    $"Line '{currentLines[id].NameSnapshot}' has no linked sale record (predates this feature) — " +
                    "can't be safely edited. Ledger correction skipped to avoid desyncing realized gains.");
            linkedMovements[id] = movement;
        }

        var resolvedLots = new Dictionary<OrderLine, InventoryLot>();
        foreach (var added in addedLines)
        {
            var lot = ctx.Lots.FirstOrDefault(l => l.Id == added.LotId)
                ?? throw new InvalidOperationException($"Lot {added.LotId} not found.");
            resolvedLots[added] = lot;
        }

        // Snapshot eBay listings for added lines' lots BEFORE any lot removal below — removing a
        // lot cascade-deletes its EbayListing row (same ordering constraint as SetStatusAsync).
        var addedLotIds = resolvedLots.Values.Select(l => l.Id).ToList();
        var ebayToEnd = ctx.EbayListings.AsNoTracking()
            .Where(e => addedLotIds.Contains(e.LotId) && e.Status == EbayListingStatus.Active)
            .ToList();

        // ---- Mutation pass + change-log accumulation ----
        var changes = new List<OrderEditChange>();
        var warnings = new List<string>();
        var restockedLots = new List<(InventoryLot Lot, int Quantity)>();

        void DiffHeaderField<T>(string field, T oldValue, T newValue, Action<T> assign)
        {
            if (EqualityComparer<T>.Default.Equals(oldValue, newValue)) return;
            changes.Add(new OrderEditChange(field, oldValue?.ToString(), newValue?.ToString()));
            assign(newValue);
        }

        DiffHeaderField("Shipping cost", order.ShippingCost, updatedHeader.ShippingCost, v => order.ShippingCost = v);
        DiffHeaderField("Shipping charged to buyer", order.ShippingChargedToBuyer, updatedHeader.ShippingChargedToBuyer, v => order.ShippingChargedToBuyer = v);
        DiffHeaderField("Marketplace fees", order.MarketplaceFees, updatedHeader.MarketplaceFees, v => order.MarketplaceFees = v);
        DiffHeaderField("Tracking number", order.TrackingNumber, updatedHeader.TrackingNumber, v => order.TrackingNumber = v);
        DiffHeaderField("Carrier", order.Carrier, updatedHeader.Carrier, v => order.Carrier = v);
        DiffHeaderField("Notes", order.Notes, updatedHeader.Notes, v => order.Notes = v);

        foreach (var id in toRemoveIds)
        {
            var line = currentLines[id];
            var created = RestoreInventory(ctx, line, line.Quantity, warnings);
            if (created is not null) restockedLots.Add((created, line.Quantity));

            ctx.Movements.Remove(linkedMovements[id]);
            ctx.OrderLines.Remove(line);
            changes.Add(new OrderEditChange($"Line '{line.NameSnapshot}' removed",
                $"x{line.Quantity} @ {line.UnitSalePrice:C}", null));
        }

        foreach (var id in toChangeIds)
        {
            var line = currentLines[id];
            var updated = updatedById[id];
            var movement = linkedMovements[id];

            var qtyDelta = line.Quantity - updated.Quantity; // >= 0, enforced by the validation pass above
            if (qtyDelta > 0)
            {
                var created = RestoreInventory(ctx, line, qtyDelta, warnings);
                if (created is not null) restockedLots.Add((created, qtyDelta));
            }

            if (updated.Quantity != line.Quantity)
            {
                changes.Add(new OrderEditChange($"Line '{line.NameSnapshot}' quantity", line.Quantity.ToString(), updated.Quantity.ToString()));
                movement.Quantity -= qtyDelta;
                line.Quantity = updated.Quantity;
            }
            if (updated.UnitSalePrice != line.UnitSalePrice)
            {
                changes.Add(new OrderEditChange($"Line '{line.NameSnapshot}' price", line.UnitSalePrice.ToString("F2"), updated.UnitSalePrice.ToString("F2")));
                movement.UnitValue = updated.UnitSalePrice;
                line.UnitSalePrice = updated.UnitSalePrice;
            }
        }

        var newLines = new List<(OrderLine NewLine, InventoryLot Lot)>();
        foreach (var added in addedLines)
        {
            var lot = resolvedLots[added];
            var product = ctx.Products.AsNoTracking().FirstOrDefault(p => p.Id == lot.ProductId);
            var newLine = new OrderLine
            {
                OrderId = orderId,
                LotId = lot.Id,
                ProductId = lot.ProductId,
                NameSnapshot = product?.Name ?? "",
                SetSnapshot = product?.SetName,
                ConditionSnapshot = lot.Condition,
                IsFoilSnapshot = product?.Foil ?? false,
                Quantity = 1,
                UnitSalePrice = added.UnitSalePrice,
            };
            ctx.OrderLines.Add(newLine);
            lot.Quantity -= 1;
            if (lot.Quantity <= 0) ctx.Lots.Remove(lot);
            newLines.Add((newLine, lot));
            changes.Add(new OrderEditChange("Line added", null, $"{newLine.NameSnapshot} x1 @ {newLine.UnitSalePrice:C}"));
        }

        if (changes.Count == 0) return; // nothing actually changed — no-op, no audit row written

        // First save: flushes header edits, removed/changed lines, new OrderLine rows (their Ids
        // are DB-generated) and lot mutations — needed before the linked Sell/Acquire movements
        // below can be created, since those need the new OrderLine.Id / InventoryLot.Id.
        await ctx.SaveChangesAsync();

        foreach (var (newLine, lot) in newLines)
        {
            ctx.Movements.Add(new InventoryMovement
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                OrderLineId = newLine.Id,
                Type = MovementType.Sell,
                Quantity = newLine.Quantity,
                UnitValue = newLine.UnitSalePrice,
                Timestamp = DateTime.UtcNow,
                Note = order.OrderNumber ?? $"Order {order.Id}",
            });
        }

        foreach (var (lot, quantity) in restockedLots)
        {
            ctx.Movements.Add(new InventoryMovement
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                Type = MovementType.Acquire,
                Quantity = quantity,
                UnitValue = lot.UnitCost,
                Timestamp = DateTime.UtcNow,
                Note = "Restored from order edit",
            });
        }

        foreach (var warning in warnings)
        {
            changes.Add(new OrderEditChange("Inventory note", null, warning));
            logger.LogWarning("EditCompletedOrder({OrderId}): {Warning}", orderId, warning);
        }

        ctx.OrderEdits.Add(new OrderEdit
        {
            OrderId = orderId,
            Timestamp = DateTime.UtcNow,
            Reason = reason,
            ChangesJson = JsonSerializer.Serialize(changes),
        });

        await ctx.SaveChangesAsync();

        // Best-effort, after the save has committed — mirrors SetStatusAsync's ship-block
        // try/catch + logger.LogError pattern; a failure here never blocks the correction itself.
        foreach (var (newLine, lot) in newLines)
            listingService.MarkSold(lot.Id, newLine.Id);

        foreach (var listing in ebayToEnd)
        {
            try
            {
                var ended = await ebayListingService.EndListingAsync(listing);
                if (!ended)
                    logger.LogError("Auto-end of eBay listing {ItemId} (lot {LotId}) after completed-order edit FAILED — end it manually on eBay.", listing.EbayItemId, listing.LotId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-end of eBay listing {ItemId} (lot {LotId}) after completed-order edit threw — end it manually on eBay.", listing.EbayItemId, listing.LotId);
            }
        }
    }

    public List<OrderEdit> GetOrderEdits(int orderId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.OrderEdits.AsNoTracking()
            .Where(e => e.OrderId == orderId)
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    /// <summary>Best-effort prorated average Acquire cost for a product, used when a lot must be
    /// recreated from scratch (original was fully consumed and removed). SQLite stores decimal as
    /// TEXT, so the aggregate must happen client-side (same caveat as GetOrderLineSummaries).</summary>
    private static decimal? BestEffortUnitCost(OmniCardDbContext ctx, int productId)
    {
        var acquires = ctx.Movements.Where(m => m.ProductId == productId && m.Type == MovementType.Acquire).ToList();
        var qty = acquires.Sum(m => m.Quantity);
        if (qty == 0) return null;
        return acquires.Sum(m => m.Quantity * (m.UnitValue ?? 0m)) / qty;
    }

    /// <summary>Restores <paramref name="quantity"/> units to inventory for a line being reduced
    /// or removed: increments the original lot if it still exists, or — if it was already fully
    /// consumed and removed at ship time — recreates a new lot (never reusing the deleted lot's
    /// id) at an unassigned location with a best-effort cost basis. Returns the newly-created lot
    /// so the caller can add its paired Acquire movement once its Id is known (post-SaveChanges),
    /// or null if an existing lot was simply incremented (that quantity was never "un-acquired" —
    /// its original Acquire movement already accounts for it).</summary>
    private static InventoryLot? RestoreInventory(OmniCardDbContext ctx, OrderLine line, int quantity, List<string> warnings)
    {
        if (line.LotId is int lotId)
        {
            var lot = ctx.Lots.FirstOrDefault(l => l.Id == lotId);
            if (lot is not null)
            {
                lot.Quantity += quantity;
                return null;
            }
        }

        if (line.ProductId is not int productId)
        {
            warnings.Add($"'{line.NameSnapshot}': couldn't restore inventory automatically (no product reference) — add stock manually.");
            return null;
        }

        var newLot = new InventoryLot
        {
            ProductId = productId,
            Quantity = quantity,
            UnitCost = BestEffortUnitCost(ctx, productId),
            Condition = line.ConditionSnapshot,
            LocationId = null,
            AcquisitionDate = DateTime.UtcNow,
            Source = "Order edit restock",
        };
        ctx.Lots.Add(newLot);
        warnings.Add($"'{line.NameSnapshot}': original lot was gone — recreated {quantity} unit(s) unassigned to a location; verify/relocate.");
        return newLot;
    }
}
