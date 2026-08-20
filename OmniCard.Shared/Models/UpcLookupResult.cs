namespace OmniCard.Models;

/// <summary>
/// Best-effort product details resolved from a UPC/barcode via an online lookup.
/// Every field is optional — a null field simply means the source didn't provide it.
/// </summary>
public sealed record UpcLookupResult(
    string? Title,
    string? Brand,
    string? Description,
    string? Category,
    string? ImageUrl);
