using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.ImportExport;
using OmniCard.Shared.Sales;

namespace OmniCard.Collection.ImportExport;

/// <summary>Template-driven CSV → orders importer. The column mapping comes from an
/// <see cref="OrderImportTemplate"/> rather than hard-coded header names, so any CSV layout can feed
/// orders. Orders are created header-only (aggregate item count + product value; no line items) — the
/// shipping-export style CSVs this targets don't carry per-card detail.</summary>
public class OrderCsvImportService(IDbContextFactory<OmniCardDbContext> dbContextFactory)
    : IOrderCsvImportService
{
    private static CsvConfiguration Config =>
        new(CultureInfo.InvariantCulture) { MissingFieldFound = null };

    public IReadOnlyList<string> ReadHeaders(string filePath)
    {
        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, Config);
        csv.Read();
        csv.ReadHeader();
        return csv.HeaderRecord ?? [];
    }

    public OrderImportPreview PreviewImport(string filePath, OrderImportTemplate template)
    {
        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, Config);
        csv.Read();
        csv.ReadHeader();

        var preview = new OrderImportPreview();
        var headers = new HashSet<string>(csv.HeaderRecord ?? [], StringComparer.OrdinalIgnoreCase);

        // Order number is the one required field: it's the dedup key and every created order needs it.
        var orderHeader = template.Map(OrderImportField.OrderNumber);
        if (orderHeader is null)
        {
            preview.Warnings.Add(
                "This template doesn't map a column to the Order Number, which is required to import orders.");
            return preview;
        }
        if (!headers.Contains(orderHeader))
        {
            preview.Warnings.Add(
                $"This file is missing the \"{orderHeader}\" column that the template maps to the Order Number.");
            return preview;
        }

        using var ctx = dbContextFactory.CreateDbContext();
        var customers = ctx.Customers.AsNoTracking().ToList();
        var existingOrderNumbers = ctx.Orders.AsNoTracking()
            .Where(o => o.OrderNumber != null)
            .Select(o => o.OrderNumber!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rowNum = 0;
        while (csv.Read())
        {
            rowNum++;
            try
            {
                var row = ParseRow(csv, template, headers, rowNum, preview.Warnings);
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

    public int Commit(OrderImportPreview preview, SalesChannel channel)
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
                Channel = channel,
                OrderNumber = row.OrderNumber,
                OrderDate = row.OrderDate,
                Status = OrderStatus.Created,
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

    private static bool IsSameCustomer(Customer c, OrderImportRow row)
        => string.Equals(c.Name, row.CustomerName, StringComparison.OrdinalIgnoreCase)
           && string.Equals(c.PostalCode ?? "", row.PostalCode ?? "", StringComparison.OrdinalIgnoreCase);

    private static void ApplyAddress(Customer c, OrderImportRow row)
    {
        c.AddressLine1 = row.AddressLine1;
        c.AddressLine2 = row.AddressLine2;
        c.City = row.City;
        c.State = row.State;
        c.PostalCode = row.PostalCode;
        c.Country = row.Country;
    }

    private static OrderImportRow ParseRow(
        CsvReader csv, OrderImportTemplate template, HashSet<string> headers, int rowNum, List<string> warnings)
    {
        // Only read headers the file actually has (a mapped-but-absent column reads as null).
        string? Field(OrderImportField field)
        {
            var header = template.Map(field);
            if (header is null || !headers.Contains(header)) return null;
            var value = csv.GetField(header);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        var name = Field(OrderImportField.FullName);
        if (string.IsNullOrWhiteSpace(name))
        {
            var first = Field(OrderImportField.FirstName) ?? "";
            var last = Field(OrderImportField.LastName) ?? "";
            name = string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        var orderNumber = Field(OrderImportField.OrderNumber) ?? "";
        var rawDate = Field(OrderImportField.OrderDate);
        var dateParsed = DateTime.TryParse(rawDate, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d);
        if (!dateParsed && rawDate is not null)
        {
            var label = string.IsNullOrWhiteSpace(orderNumber) ? $"Row {rowNum}" : $"Order {orderNumber}";
            warnings.Add($"{label}: couldn't parse Order Date \"{rawDate}\"; defaulted to today.");
        }

        return new OrderImportRow
        {
            OrderNumber = orderNumber,
            CustomerName = name,
            AddressLine1 = Field(OrderImportField.Address1),
            AddressLine2 = Field(OrderImportField.Address2),
            City = Field(OrderImportField.City),
            State = Field(OrderImportField.State),
            PostalCode = Field(OrderImportField.PostalCode),
            Country = Field(OrderImportField.Country),
            OrderDate = dateParsed ? d : DateTime.UtcNow.Date,
            ShippingFeePaid = ParseDecimal(Field(OrderImportField.ShippingFeePaid)),
            ItemCount = int.TryParse(Field(OrderImportField.ItemCount), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var ic) ? ic : 0,
            ValueOfProducts = ParseDecimal(Field(OrderImportField.ValueOfProducts)),
            TrackingNumber = Field(OrderImportField.TrackingNumber),
            Carrier = Field(OrderImportField.Carrier),
        };
    }

    private static decimal ParseDecimal(string? s)
        => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}
