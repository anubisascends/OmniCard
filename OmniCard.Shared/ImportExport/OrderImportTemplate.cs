using OmniCard.Shared.Sales;

namespace OmniCard.Shared.ImportExport;

/// <summary>The logical order/customer fields a CSV column can be mapped onto. A template maps each of
/// these to a CSV header name; unmapped fields are simply not populated.</summary>
public enum OrderImportField
{
    /// <summary>Full customer name in a single column. If mapped, it wins over FirstName/LastName.</summary>
    FullName,
    FirstName,
    LastName,
    OrderNumber,
    OrderDate,
    Address1,
    Address2,
    City,
    State,
    PostalCode,
    Country,
    ShippingFeePaid,
    ItemCount,
    ValueOfProducts,
    TrackingNumber,
    Carrier,
}

/// <summary>A reusable column-mapping for importing orders from a CSV. Maps each logical
/// <see cref="OrderImportField"/> to a CSV header name. Built-in templates ship with the app and can't be
/// edited or deleted; custom templates are user-defined and persisted in the data directory.</summary>
public class OrderImportTemplate
{
    /// <summary>Stable slug id (e.g. <c>tcgplayer</c> or a slug derived from a custom name).</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Built-in templates are merged in at read time and can't be overwritten or removed.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>The sales channel new orders from this template are recorded under.</summary>
    public SalesChannel Channel { get; set; } = SalesChannel.Manual;

    /// <summary>Logical field name (see <see cref="OrderImportField"/>) → CSV header name.</summary>
    public Dictionary<string, string> ColumnMappings { get; set; } = new();

    /// <summary>The CSV header a field is mapped to, or null if the field is unmapped/blank.</summary>
    public string? Map(OrderImportField field) =>
        ColumnMappings.TryGetValue(field.ToString(), out var header) && !string.IsNullOrWhiteSpace(header)
            ? header.Trim()
            : null;

    /// <summary>The built-in TCGPlayer Shipping Export template, pre-mapped to that file's column headers.</summary>
    public static OrderImportTemplate TcgPlayerDefault() => new()
    {
        Id = "tcgplayer",
        Name = "TCGPlayer Shipping Export",
        IsBuiltIn = true,
        Channel = SalesChannel.TcgPlayer,
        ColumnMappings = new(StringComparer.Ordinal)
        {
            [nameof(OrderImportField.FirstName)] = "FirstName",
            [nameof(OrderImportField.LastName)] = "LastName",
            [nameof(OrderImportField.OrderNumber)] = "Order #",
            [nameof(OrderImportField.OrderDate)] = "Order Date",
            [nameof(OrderImportField.Address1)] = "Address1",
            [nameof(OrderImportField.Address2)] = "Address2",
            [nameof(OrderImportField.City)] = "City",
            [nameof(OrderImportField.State)] = "State",
            [nameof(OrderImportField.PostalCode)] = "PostalCode",
            [nameof(OrderImportField.Country)] = "Country",
            [nameof(OrderImportField.ShippingFeePaid)] = "Shipping Fee Paid",
            [nameof(OrderImportField.ItemCount)] = "Item Count",
            [nameof(OrderImportField.ValueOfProducts)] = "Value Of Products",
            [nameof(OrderImportField.TrackingNumber)] = "Tracking #",
            [nameof(OrderImportField.Carrier)] = "Carrier",
        },
    };

    /// <summary>The built-in templates shipped with the app.</summary>
    public static IReadOnlyList<OrderImportTemplate> BuiltIns() => [TcgPlayerDefault()];
}
