namespace OmniCard.Shared.ImportExport;

/// <summary>Stores reusable order-import column-mapping templates. Built-in templates (e.g. the TCGPlayer
/// Shipping Export) are always present and can't be edited or deleted; custom templates are persisted in
/// the data directory.</summary>
public interface IOrderImportTemplateService
{
    /// <summary>All templates — built-ins first, then custom, ordered by name.</summary>
    IReadOnlyList<OrderImportTemplate> GetAll();

    /// <summary>A single template by id (built-in or custom), or null if none matches.</summary>
    OrderImportTemplate? Get(string id);

    /// <summary>Creates or updates a custom template and returns the saved copy (with its assigned id).
    /// Throws <see cref="InvalidOperationException"/> if the id refers to a built-in template.</summary>
    OrderImportTemplate Save(OrderImportTemplate template);

    /// <summary>Deletes a custom template. Returns false if it doesn't exist or is a built-in.</summary>
    bool Delete(string id);
}
