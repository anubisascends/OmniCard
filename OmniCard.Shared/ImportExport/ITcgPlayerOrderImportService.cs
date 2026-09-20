namespace OmniCard.Shared.ImportExport;

/// <summary>Convenience wrapper over <see cref="IOrderCsvImportService"/> pinned to the built-in
/// TCGPlayer Shipping Export template. Retained for backward compatibility.</summary>
public interface ITcgPlayerOrderImportService
{
    /// <summary>Parses a TCGPlayer Shipping Export and resolves customer-match / duplicate-order
    /// status for each row against the current database.</summary>
    OrderImportPreview PreviewImport(string filePath);

    /// <summary>Creates customers/orders for the included, non-duplicate rows. Idempotent:
    /// order numbers that already exist are skipped. Returns the number of orders created.</summary>
    int Commit(OrderImportPreview preview);
}
