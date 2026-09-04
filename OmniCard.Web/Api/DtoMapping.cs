using OmniCard.Api.Contracts;
using OmniCard.Models;

namespace OmniCard.Web.Api;

/// <summary>Maps internal domain models to the client-facing API DTOs, so MVVM/EF types never
/// leak into JSON responses.</summary>
public static class DtoMapping
{
    public static string GameId(CardGame game) => game.ToString();

    public static string GameDisplayName(CardGame game) => game switch
    {
        CardGame.Mtg => "Magic: The Gathering",
        CardGame.OnePiece => "One Piece TCG",
        CardGame.Riftbound => "Riftbound",
        CardGame.Pokemon => "Pokémon",
        CardGame.YuGiOh => "Yu-Gi-Oh!",
        CardGame.FinalFantasy => "Final Fantasy TCG",
        _ => game.ToString(),
    };

    public static GameDto ToDto(CardGame game) => new(GameId(game), GameDisplayName(game));

    public static string ContainerTypeDisplay(ContainerType type) => type switch
    {
        ContainerType.Bulk => "Bulk",
        ContainerType.Binder => "Binder",
        ContainerType.Box => "Box",
        ContainerType.DeckBox => "Deck Box",
        ContainerType.DisplayCase => "Display Case",
        _ => type.ToString(),
    };

    public static CardDto ToDto(CollectionCard c) => new()
    {
        Id = c.Id,
        Game = GameId(c.Game),
        GameCardId = c.GameCardId,
        Name = c.Name,
        SetName = c.SetName,
        SetCode = c.SetCode,
        Number = c.Number,
        Rarity = c.Rarity,
        ImageUri = c.ImageUri,
        ScanImagePath = c.ScanImagePath,
        Condition = c.Condition,
        IsFoil = c.IsFoil,
        FoilType = c.FoilType,
        Quantity = c.Quantity,
        Tags = c.Tags.ToList(),
        PurchasePrice = c.PurchasePrice,
        MarketPrice = c.MarketPrice,
        ContainerId = c.ContainerId,
        ContainerName = c.Container?.Name,
        Page = c.Page,
        Slot = c.Slot,
        Section = c.Section,
        Color = c.Color,
        CardType = c.CardType,
        IsMissing = c.IsMissing,
        IsTraded = c.IsTraded,
    };

    public static LocationSummaryDto ToDto(LocationTileSummary s) => new()
    {
        Id = s.Container.Id,
        Name = s.Container.Name,
        Type = ContainerTypeDisplay(s.Container.ContainerType),
        IsSystem = s.Container.IsSystem,
        IsAlwaysAvailable = s.Container.IsSystem || s.Container.AlwaysAvailable,
        CardCount = s.CardCount,
        UniquePrintCount = s.UniquePrintCount,
        TotalMarketValue = s.TotalMarketValue,
        TotalPurchaseCost = s.TotalPurchaseCost,
        PriceDelta = s.PriceDelta,
        PriceDeltaPercent = s.PriceDeltaPercent,
        CoverImageUri = s.CoverImageUri,
    };

    public static ValuationLineDto ToDto(ValuationLine v) => new(v.Key, v.Units, v.Cost, v.Market);

    public static SetInfoDto ToDto(SetInfo s) => new(s.SetCode, s.SetName);

    public static SetChecklistDto ToDto(SetChecklist c) => new()
    {
        Game = GameId(c.Game),
        SetCode = c.SetCode,
        SetName = c.SetName,
        OwnedCount = c.OwnedCount,
        TotalCount = c.TotalCount,
        OwnedPhysicalCount = c.OwnedPhysicalCount,
        CompletionPercent = c.CompletionPercent,
        Cards = c.Cards.Select(ToDto).ToList(),
    };

    public static SetChecklistCardDto ToDto(SetChecklistCard c) => new()
    {
        GameCardId = c.GameCardId,
        CollectorNumber = c.CollectorNumber,
        Name = c.Name,
        Rarity = c.Rarity,
        ImageUri = c.Card.ImageUri,
        OwnedQuantity = c.OwnedQuantity,
        NormalPrice = c.NormalPrice,
        FoilPrice = c.FoilPrice,
        HasFoil = c.HasFoil,
    };

    public static DashboardDto ToDto(HoldingsValuation h, RealizedSummary r) => new()
    {
        TotalUnits = h.TotalUnits,
        TotalCost = h.TotalCost,
        TotalMarket = h.TotalMarket,
        UnrealizedDelta = h.TotalMarket - h.TotalCost,
        ByGame = h.ByGame.Select(ToDto).ToList(),
        ByCategory = h.ByCategory.Select(ToDto).ToList(),
        ByLocation = h.ByLocation.Select(ToDto).ToList(),
        Realized = new RealizedDto
        {
            TotalSold = r.TotalSold,
            TotalProceeds = r.TotalProceeds,
            TotalCost = r.TotalCost,
            TotalFees = r.TotalFees,
            Profit = r.TotalProceeds - r.TotalCost - r.TotalFees,
        },
    };
}
