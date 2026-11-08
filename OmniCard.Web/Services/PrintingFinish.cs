using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;

namespace OmniCard.Web.Services;

/// <summary>
/// Whether a catalog printing exists in a single finish — so picking it as a scan correction can set the
/// copy's foil flag too. <c>Foil</c>: true = foil only, false = non-foil only, null = both or unknown.
/// <c>FoilType</c> is only given when the finish is specific (MTG etched-only); otherwise the copy keeps
/// whatever foil type it already had.
/// </summary>
public static class PrintingFinish
{
    public static (bool? Foil, string? FoilType) Of(object? source) => source switch
    {
        // Scryfall lists every finish a printing was made in.
        Card c => FromFinishes(c.Finishes),
        // TCGCSV lists one price row per subtype a product sells in: a foil row and no normal row means the
        // printing is foil only. (No price at all, or both, says nothing. Yu-Gi-Oh!'s subtypes are editions,
        // not finishes, so it is never read this way.)
        TcgCsvCard c when c.Game != CardGame.YuGiOh => FromPrices(c.MarketPrice ?? c.LowPrice, c.FoilMarketPrice ?? c.FoilLowPrice),
        RiftboundCard c => FromPrices(c.MarketPrice ?? c.LowPrice, c.FoilMarketPrice ?? c.FoilLowPrice),
        _ => (null, null),
    };

    private static (bool?, string?) FromFinishes(IReadOnlyCollection<string>? finishes)
    {
        if (finishes is null || finishes.Count == 0) return (null, null);
        bool Has(string f) => finishes.Any(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase));
        var nonFoil = Has("nonfoil");
        var foil = Has("foil");
        var etched = Has("etched");
        if (nonFoil && !foil && !etched) return (false, null);
        if (!nonFoil && etched && !foil) return (true, "Etched");
        if (!nonFoil && foil) return (true, null);
        return (null, null);
    }

    private static (bool?, string?) FromPrices(decimal? normal, decimal? foil) =>
        normal is null && foil is not null ? (true, null) : (null, null);
}
