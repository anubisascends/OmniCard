using OmniCard.Models;

namespace OmniCard.Interfaces;

/// <summary>
/// Resolves product details from a UPC/barcode via an online catalog, so that scanning a
/// barcode can prefill a new sealed product with as little manual entry as possible.
/// Implementations are best-effort: they return <c>null</c> (rather than throwing) when the
/// lookup fails or the UPC is unknown, so callers can silently fall back to manual entry.
/// </summary>
public interface IUpcLookupService
{
    Task<UpcLookupResult?> LookupAsync(string upc, CancellationToken cancellationToken = default);
}
