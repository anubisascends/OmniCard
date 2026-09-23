using System.ComponentModel;
using ModelContextProtocol.Server;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Sales;
using OmniCard.Web.Api.Mapping;

namespace OmniCard.Web.Mcp.Tools;

/// <summary>Read-only MCP tools over sales: orders, their line items, and customers. Mirrors the read
/// paths of <c>OrdersController</c> / <c>CustomersController</c>.</summary>
[McpServerToolType]
public sealed class SalesTools(IOrderService orders, ICustomerService customers)
{
    [McpServerTool(Name = "list_orders")]
    [Description("List all sales orders (newest first), each with customer name, item count, and total.")]
    public IReadOnlyList<OrderDto> ListOrders()
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

    [McpServerTool(Name = "get_order")]
    [Description("Get one sales order by id: its header plus every line item (card, condition, qty, price).")]
    public OrderDetailDto? GetOrder([Description("The order id.")] int id)
    {
        var order = orders.GetOrder(id);
        if (order is null) return null;

        order.CustomerNameDisplay = customers.Get(order.CustomerId)?.Name;
        var lines = orders.GetLines(id);
        order.LineItemCount = lines.Sum(l => l.Quantity);
        order.LineTotal = lines.Sum(l => l.UnitSalePrice * l.Quantity);

        var lineDtos = lines.Select(l => new OrderLineDto(
            l.Id, l.LotId, l.NameSnapshot, l.SetSnapshot, l.ConditionSnapshot, l.IsFoilSnapshot,
            l.Quantity, l.UnitSalePrice)).ToList();
        return new OrderDetailDto(DtoMapping.ToDto(order), lineDtos);
    }

    [McpServerTool(Name = "list_customers")]
    [Description("List all sales customers with their contact details.")]
    public IReadOnlyList<CustomerDto> ListCustomers() =>
        customers.GetAll().Select(DtoMapping.ToDto).ToList();
}
