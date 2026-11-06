using OmniCard.Data;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Storage;

namespace OmniCard.Collection.Inventory;

/// <summary>The single place that keeps <see cref="InventoryLot.OriginLocationId"/> (and its page, slot and
/// section) up to date when a copy changes location. Every path that moves a lot to another location calls
/// <see cref="BeforeMove"/> <em>before</em> it overwrites the lot's location, or <see cref="ForSplit"/> when
/// it splits copies off a stack into a new lot at the destination. The rule:
/// <list type="bullet">
/// <item>into a deck box from anywhere that isn't a deck box → remember where it was;</item>
/// <item>from one deck box into another → keep the origin it already has (the first non-deck location);</item>
/// <item>into anything that isn't a deck box → forget the origin (the copy is out of the decks).</item>
/// </list>
/// A move within the same location changes nothing.</summary>
public static class DeckOrigin
{
    /// <summary>Updates <paramref name="lot"/>'s origin for a move to <paramref name="targetLocationId"/>.
    /// Call while the lot still has its current location, page, slot and section.</summary>
    public static void BeforeMove(OmniCardDbContext context, InventoryLot lot, int? targetLocationId)
    {
        if (lot.LocationId == targetLocationId)
            return;
        Apply(lot, lot, IsDeckBox(context, lot.LocationId), IsDeckBox(context, targetLocationId));
    }

    /// <summary>Sets the origin of <paramref name="split"/>, a new lot holding copies split off
    /// <paramref name="parent"/> (which stays where it is) into <paramref name="targetLocationId"/>.</summary>
    public static void ForSplit(OmniCardDbContext context, InventoryLot parent, InventoryLot split, int? targetLocationId)
    {
        if (parent.LocationId == targetLocationId)
        {
            CopyOrigin(parent, split);
            return;
        }
        Apply(parent, split, IsDeckBox(context, parent.LocationId), IsDeckBox(context, targetLocationId));
    }

    /// <summary>Copies the origin fields from one lot to another (a split that stays in the same place).</summary>
    public static void CopyOrigin(InventoryLot from, InventoryLot to)
    {
        to.OriginLocationId = from.OriginLocationId;
        to.OriginPage = from.OriginPage;
        to.OriginSlot = from.OriginSlot;
        to.OriginSection = from.OriginSection;
    }

    /// <summary>Forgets the origin (the copy went back, or left the deck boxes).</summary>
    public static void Clear(InventoryLot lot)
    {
        lot.OriginLocationId = null;
        lot.OriginPage = null;
        lot.OriginSlot = null;
        lot.OriginSection = null;
    }

    /// <summary>True when <paramref name="locationId"/> is a deck box.</summary>
    public static bool IsDeckBox(OmniCardDbContext context, int? locationId) =>
        locationId is int id
        && context.StorageContainers.Where(c => c.Id == id).Select(c => c.ContainerType).FirstOrDefault()
            == ContainerType.DeckBox;

    // `source` holds the position being left; `target` is the lot whose origin is written (the same lot for a
    // whole-lot move, the new lot for a split).
    private static void Apply(InventoryLot source, InventoryLot target, bool fromDeck, bool toDeck)
    {
        if (!toDeck)
        {
            Clear(target);
            return;
        }
        if (fromDeck)
        {
            if (!ReferenceEquals(source, target))
                CopyOrigin(source, target);
            return;
        }
        if (source.LocationId is null)
        {
            Clear(target);
            return;
        }
        target.OriginLocationId = source.LocationId;
        target.OriginPage = source.Page;
        target.OriginSlot = source.Slot;
        target.OriginSection = source.Section;
    }
}
