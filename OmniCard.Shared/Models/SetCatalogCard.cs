namespace OmniCard.Models;

/// <summary>One printing in a set's full catalog, independent of ownership. Produced by
/// <see cref="OmniCard.Interfaces.ICardGameService.GetSetCards"/> to drive the Sets tab
/// checklist and the printable want-list. Prices are the current catalog market prices
/// (not persisted lot prices): <see cref="NormalPrice"/> is the standard/non-foil price and
/// <see cref="FoilPrice"/> the principal foil finish, either of which may be null.</summary>
public class SetCatalogCard
{
    public string GameCardId { get; init; } = "";
    public string Name { get; init; } = "";
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";
    public string CollectorNumber { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string? ImageUri { get; init; }
    public string? LocalImagePath { get; init; }
    public decimal? NormalPrice { get; init; }
    public decimal? FoilPrice { get; init; }

    /// <summary>True when this game/printing has a distinct foil finish (i.e. a foil price is meaningful).</summary>
    public bool HasFoil { get; init; }
}
