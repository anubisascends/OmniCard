namespace OmniCard.Shared.Matching;

/// <summary>What a Cyberpunk TCG card prints in its lower-left corner: the product code running up the
/// left edge ("MS01 - WNC [A]") and the collector number in the diagonal yellow box ("120", or "β120"
/// on Beta printings). Any part can be null when it couldn't be read.</summary>
/// <param name="Product">The product line, e.g. "MS01".</param>
/// <param name="SetCode">The set's printed code, e.g. "WNC" (Welcome to Night City).</param>
/// <param name="Variant">The bracketed letter after the set code, e.g. "A".</param>
/// <param name="Number">The collector number without the Beta mark, e.g. "120".</param>
/// <param name="Beta">True when a β precedes the number (the Beta printing).</param>
public sealed record CyberpunkPrintedCode(string? Product, string? SetCode, string? Variant, string? Number, bool Beta)
{
    public static readonly CyberpunkPrintedCode Empty = new(null, null, null, null, false);

    /// <summary>The number as the TCGCSV catalog stores it: Beta printings carry a "B" prefix ("B120").</summary>
    public string? CatalogNumber => Number is null ? null : Beta ? "B" + Number : Number;
}
