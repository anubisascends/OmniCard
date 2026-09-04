using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Api;

/// <summary>Orders board: the customizable kanban lanes, the orders, and status/lane changes
/// (the drag-between-lanes action). Backed by <see cref="IOrderService"/>.</summary>
public sealed class OrdersController(
    IOrderService orders,
    ICustomerService customers,
    ISalesSettingsService settings) : ApiControllerBase
{
    /// <summary>The kanban lanes in board order (customizable; falls back to built-in defaults).</summary>
    [HttpGet("lanes")]
    public ActionResult<IReadOnlyList<WorkflowLaneDto>> Lanes() =>
        settings.GetWorkflowLanes().Select(DtoMapping.ToDto).ToList();

    /// <summary>All orders, newest first, each with its customer name + line count/total for the
    /// kanban cards.</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<OrderDto>> Get()
    {
        var summaries = orders.GetOrderLineSummaries().ToDictionary(s => s.OrderId);
        var customerNames = customers.GetAll().ToDictionary(c => c.Id, c => c.Name);

        var list = orders.GetOrders();
        foreach (var o in list)
        {
            o.CustomerNameDisplay = customerNames.GetValueOrDefault(o.CustomerId);
            if (summaries.TryGetValue(o.Id, out var s))
            {
                o.LineItemCount = s.ItemCount;
                o.LineTotal = s.Total;
            }
        }
        return list.Select(DtoMapping.ToDto).ToList();
    }

    /// <summary>Move an order to a new status/lane (kanban drag). On a transition to Shipped this
    /// records the sale + marks listings sold (eBay auto-end is stubbed until Phase 5).</summary>
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] SetOrderStatusRequest req)
    {
        if (!Enum.TryParse<OrderStatus>(req.Status, ignoreCase: true, out var status))
            return BadRequest(new { error = $"Invalid status '{req.Status}'." });
        await orders.SetStatusAsync(id, status, req.StageKey);
        return NoContent();
    }
}
