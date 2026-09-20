using OmniCard.Shared.Sales;

namespace OmniCard.Shared.ImportExport;

/// <summary>Template-driven CSV → orders importer. Parses a CSV using an <see cref="OrderImportTemplate"/>
/// column mapping, resolves customer-match / duplicate-order status against the current database, and
/// commits the selected rows as header-only orders (aggregate item count + product value; no line items).</summary>
public interface IOrderCsvImportService
{
    /// <summary>The raw column headers of a CSV, in file order — used to drive the mapping UI.</summary>
    IReadOnlyList<string> ReadHeaders(string filePath);

    /// <summary>Parses the CSV under <paramref name="template"/>'s mapping and resolves per-row
    /// customer-match / duplicate-order status against the current database.</summary>
    OrderImportPreview PreviewImport(string filePath, OrderImportTemplate template);

    /// <summary>Creates customers/orders for the included, non-duplicate rows under
    /// <paramref name="channel"/>. Idempotent: order numbers that already exist are skipped. Returns the
    /// number of orders created.</summary>
    int Commit(OrderImportPreview preview, SalesChannel channel);
}
