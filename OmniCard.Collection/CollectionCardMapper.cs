using OmniCard.Models;

namespace OmniCard.Collection;

/// <summary>
/// Projects an <see cref="InventoryLot"/> + its owning <see cref="Product"/> into the
/// read-only <see cref="CollectionCard"/> DTO shape the UI (and, later, the Web app) expect.
/// This is the single source of truth for that projection — keep read call sites going
/// through here rather than hand-rolling the field mapping.
/// </summary>
public static class CollectionCardMapper
{
    public static CollectionCard ToDto(InventoryLot lot, Product product, decimal marketPrice)
    {
        return new CollectionCard
        {
            Id = lot.Id,

            // Identity fields come from the Product (shared across all lots of the same printing/foil).
            Game = product.Game,
            GameCardId = product.GameCardId ?? "",
            Name = product.Name,
            SetName = product.SetName ?? "",
            SetCode = product.SetCode ?? "",
            Number = product.CollectorNumber ?? "",
            Rarity = product.Rarity ?? "",
            ImageUri = product.ImageUri,
            IsFoil = product.Foil,
            Color = product.Color,
            CardType = product.CardType,

            // Copy attributes come from the Lot (unique per physical copy).
            Condition = lot.Condition ?? "NM",
            ScanImagePath = lot.ScanImagePath,
            Page = lot.Page,
            Slot = lot.Slot,
            Section = lot.Section,
            PurchasePrice = lot.UnitCost,
            DateAdded = lot.AcquisitionDate,
            ContainerId = lot.LocationId,
            IsMissing = lot.IsMissing,
            FlagReason = lot.FlagReason,

            MarketPrice = marketPrice,
        };
    }
}
