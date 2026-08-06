using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class ReceiptService(
    IOrderService orderService,
    ICustomerService customerService,
    ISalesSettingsService salesSettings,
    IDataPathService dataPathService) : IReceiptService
{
    public ReceiptDocument BuildReceipt(int orderId)
    {
        var order = orderService.GetOrder(orderId)
                    ?? throw new InvalidOperationException($"Order {orderId} not found.");
        var lines = orderService.GetLines(orderId);
        var customer = customerService.Get(order.CustomerId);
        var company = salesSettings.GetCompany();
        var receipt = salesSettings.GetReceipt();

        // Identical items (same name/set/condition/foil/price) collapse into one printed line
        // with a summed quantity — avoids one paper line per physical lot for bulk sales.
        var receiptLines = lines
            .GroupBy(l => l.GroupKey)
            .Select(g =>
            {
                var first = g.First();
                var quantity = g.Sum(l => l.Quantity);
                return new ReceiptLine
                {
                    Name = first.NameSnapshot,
                    Set = first.SetSnapshot,
                    Condition = first.ConditionSnapshot,
                    IsFoil = first.IsFoilSnapshot,
                    Quantity = quantity,
                    UnitSalePrice = first.UnitSalePrice,
                    LineTotal = quantity * first.UnitSalePrice,
                };
            }).ToList();

        var itemsTotal = receiptLines.Sum(l => l.LineTotal);

        string? logoAbs = null;
        if (!string.IsNullOrWhiteSpace(company.LogoPath))
        {
            var candidate = Path.Combine(dataPathService.DataDirectory, company.LogoPath);
            if (File.Exists(candidate)) logoAbs = candidate;
        }

        return new ReceiptDocument
        {
            CompanyName = company.Name,
            CompanyAddressBlock = JoinBlock(
                company.AddressLine1, company.AddressLine2,
                JoinInline(company.City, company.State, company.PostalCode), company.Country),
            CompanyLogoAbsolutePath = logoAbs,
            CompanyEmail = company.Email,
            CompanyPhone = company.Phone,

            OrderNumber = order.OrderNumber,
            OrderDate = order.OrderDate,
            TrackingNumber = order.TrackingNumber,
            Carrier = order.Carrier,

            CustomerName = customer?.Name ?? "",
            CustomerAddressBlock = customer is null ? null : JoinBlock(
                customer.AddressLine1, customer.AddressLine2,
                JoinInline(customer.City, customer.State, customer.PostalCode), customer.Country),

            Lines = receiptLines,
            ShowPrices = receipt.ShowPrices,
            ItemsTotal = itemsTotal,
            Shipping = order.ShippingChargedToBuyer,
            GrandTotal = itemsTotal + order.ShippingChargedToBuyer,
            FooterText = receipt.FooterText,

            WidthMm = receipt.WidthMm,
            MarginMm = receipt.MarginMm,
            FontPointSize = receipt.FontPointSize,
        };
    }

    /// <summary>Joins non-empty parts with newlines (multi-line address block).</summary>
    private static string? JoinBlock(params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return kept.Count == 0 ? null : string.Join("\n", kept);
    }

    /// <summary>Joins non-empty parts with spaces (e.g. "City ST 12345").</summary>
    private static string? JoinInline(params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return kept.Count == 0 ? null : string.Join(" ", kept);
    }
}
