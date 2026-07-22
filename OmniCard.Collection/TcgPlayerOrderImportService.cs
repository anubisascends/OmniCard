using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class TcgPlayerOrderImportService(IDbContextFactory<OmniCardDbContext> dbContextFactory)
    : ITcgPlayerOrderImportService
{
    public TcgOrderImportPreview PreviewImport(string filePath)
    {
        using var reader = new StreamReader(filePath);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { MissingFieldFound = null };
        using var csv = new CsvReader(reader, config);
        csv.Read();
        csv.ReadHeader();

        using var ctx = dbContextFactory.CreateDbContext();
        var customers = ctx.Customers.AsNoTracking().ToList();
        var existingOrderNumbers = ctx.Orders.AsNoTracking()
            .Where(o => o.OrderNumber != null)
            .Select(o => o.OrderNumber!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var preview = new TcgOrderImportPreview();
        var rowNum = 0;
        while (csv.Read())
        {
            rowNum++;
            try
            {
                var row = ParseRow(csv);
                var match = customers.FirstOrDefault(c => IsSameCustomer(c, row));
                row.MatchedCustomerId = match?.Id;
                row.IsNewCustomer = match is null;
                row.IsDuplicateOrder = !string.IsNullOrWhiteSpace(row.OrderNumber)
                                       && existingOrderNumbers.Contains(row.OrderNumber);
                row.Include = !row.IsDuplicateOrder;
                preview.Rows.Add(row);
            }
            catch (Exception ex)
            {
                preview.Warnings.Add($"Row {rowNum}: {ex.Message}");
            }
        }
        return preview;
    }

    public int Commit(TcgOrderImportPreview preview)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var customers = ctx.Customers.ToList(); // tracked
        var seenOrderNumbers = ctx.Orders
            .Where(o => o.OrderNumber != null)
            .Select(o => o.OrderNumber!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = 0;
        foreach (var row in preview.Rows.Where(r => r.Include && !r.IsDuplicateOrder))
        {
            // Idempotent + intra-file dedup: skip blank or already-seen order numbers.
            if (string.IsNullOrWhiteSpace(row.OrderNumber) || !seenOrderNumbers.Add(row.OrderNumber))
                continue;

            var customer = customers.FirstOrDefault(c => IsSameCustomer(c, row));
            if (customer is null)
            {
                customer = new Customer { Name = row.CustomerName, CreatedAt = DateTime.UtcNow };
                ApplyAddress(customer, row);
                ctx.Customers.Add(customer);
                ctx.SaveChanges();          // assign Id
                customers.Add(customer);    // so a repeat buyer later in the file reuses it
            }
            else
            {
                ApplyAddress(customer, row); // refresh address; persisted in the final SaveChanges
            }

            ctx.Orders.Add(new Order
            {
                CustomerId = customer.Id,
                Channel = SalesChannel.TcgPlayer,
                OrderNumber = row.OrderNumber,
                OrderDate = row.OrderDate,
                Status = OrderStatus.Open,
                ShippingChargedToBuyer = row.ShippingFeePaid,
                TrackingNumber = row.TrackingNumber,
                Carrier = row.Carrier,
                ImportedItemCount = row.ItemCount,
                ImportedProductValue = row.ValueOfProducts,
                CreatedAt = DateTime.UtcNow,
            });
            created++;
        }

        ctx.SaveChanges();
        return created;
    }

    private static bool IsSameCustomer(Customer c, TcgOrderImportRow row)
        => string.Equals(c.Name, row.CustomerName, StringComparison.OrdinalIgnoreCase)
           && string.Equals(c.PostalCode ?? "", row.PostalCode ?? "", StringComparison.OrdinalIgnoreCase);

    private static void ApplyAddress(Customer c, TcgOrderImportRow row)
    {
        c.AddressLine1 = row.AddressLine1;
        c.AddressLine2 = row.AddressLine2;
        c.City = row.City;
        c.State = row.State;
        c.PostalCode = row.PostalCode;
        c.Country = row.Country;
    }

    private static TcgOrderImportRow ParseRow(CsvReader csv)
    {
        var first = csv.GetField("FirstName")?.Trim() ?? "";
        var last = csv.GetField("LastName")?.Trim() ?? "";
        var name = string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return new TcgOrderImportRow
        {
            OrderNumber = csv.GetField("Order #")?.Trim() ?? "",
            CustomerName = name,
            AddressLine1 = NullIfBlank(csv.GetField("Address1")),
            AddressLine2 = NullIfBlank(csv.GetField("Address2")),
            City = NullIfBlank(csv.GetField("City")),
            State = NullIfBlank(csv.GetField("State")),
            PostalCode = NullIfBlank(csv.GetField("PostalCode")),
            Country = NullIfBlank(csv.GetField("Country")),
            OrderDate = DateTime.TryParse(csv.GetField("Order Date"), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d) ? d : DateTime.UtcNow.Date,
            ShippingFeePaid = ParseDecimal(csv.GetField("Shipping Fee Paid")),
            ItemCount = int.TryParse(csv.GetField("Item Count"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var ic) ? ic : 0,
            ValueOfProducts = ParseDecimal(csv.GetField("Value Of Products")),
            TrackingNumber = NullIfBlank(csv.GetField("Tracking #")),
            Carrier = NullIfBlank(csv.GetField("Carrier")),
        };
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static decimal ParseDecimal(string? s)
        => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}
