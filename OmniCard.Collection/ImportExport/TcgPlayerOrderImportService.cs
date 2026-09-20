using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.ImportExport;
using OmniCard.Shared.Sales;

namespace OmniCard.Collection.ImportExport;

/// <summary>Backward-compatible wrapper that runs <see cref="OrderCsvImportService"/> with the built-in
/// TCGPlayer Shipping Export template pinned to the TCGPlayer channel.</summary>
public class TcgPlayerOrderImportService(IDbContextFactory<OmniCardDbContext> dbContextFactory)
    : ITcgPlayerOrderImportService
{
    private readonly OrderCsvImportService _inner = new(dbContextFactory);

    public OrderImportPreview PreviewImport(string filePath)
        => _inner.PreviewImport(filePath, OrderImportTemplate.TcgPlayerDefault());

    public int Commit(OrderImportPreview preview)
        => _inner.Commit(preview, SalesChannel.TcgPlayer);
}
