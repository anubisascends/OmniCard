using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Storage;

namespace OmniCard.Collection.Lists;

/// <summary>Checks a deck box's owned singles against its deck type's build rules (deck size, copy
/// limits / singleton, commander count), reusing <see cref="DeckCardClassifier"/>'s reserved
/// commander/sideboard tags. Every finding is a non-blocking warning — this never prevents an
/// action, it only advises.</summary>
public sealed class DeckLegalityService(
    IDbContextFactory<OmniCardDbContext> dbContextFactory,
    IDeckTypeService deckTypes) : IDeckLegalityService
{
    // The five basic land names plus Wastes; matched case-insensitively for the singleton/copy-limit
    // exemption (a Commander deck may run any number of basics).
    private static readonly HashSet<string> BasicLandNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes",
    };

    public DeckLegality Check(int deckBoxId)
    {
        using var context = dbContextFactory.CreateDbContext();
        var box = context.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == deckBoxId);
        if (box is null || box.ContainerType != ContainerType.DeckBox || box.DeckTypeId is not int deckTypeId)
            return new DeckLegality();

        var deckType = deckTypes.GetById(deckTypeId);
        if (deckType is null)
            return new DeckLegality();

        // Owned singles in the box, with the data the rules need. Tags come from the join tables.
        var lots = context.Lots.AsNoTracking()
            .Where(l => l.LocationId == deckBoxId && l.Product.Category == ProductCategory.Single)
            .Select(l => new { l.Id, l.Quantity, l.Product.Name, l.Product.CardType })
            .ToList();

        var lotIds = lots.Select(l => l.Id).ToList();
        var tagsByLot = (from lt in context.LotTags.AsNoTracking()
                         join t in context.Tags.AsNoTracking() on lt.TagId equals t.Id
                         where lotIds.Contains(lt.LotId)
                         select new { lt.LotId, t.Name })
            .ToList()
            .GroupBy(x => x.LotId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());

        var warnings = new List<DeckLegalityWarning>();
        var commanderCount = 0;
        var mainDeckCount = 0;
        var mainCopiesByName = new Dictionary<string, (int Count, bool IsBasicLand)>(StringComparer.OrdinalIgnoreCase);

        foreach (var lot in lots)
        {
            var tags = tagsByLot.GetValueOrDefault(lot.Id);
            var (isCommander, isSideboard) = ReadReservedTags(tags);
            var qty = Math.Max(1, lot.Quantity);

            if (isCommander) { commanderCount += qty; continue; }
            if (isSideboard) continue;   // sideboard is outside the main-deck rules

            mainDeckCount += qty;
            var name = (lot.Name ?? "").Trim();
            var isBasic = IsBasicLand(name, lot.CardType);
            var prev = mainCopiesByName.GetValueOrDefault(name);
            mainCopiesByName[name] = (prev.Count + qty, isBasic);
        }

        // --- Deck size ---
        // The commander/leader is part of the deck-size total: Commander is 1 commander + 99 = 100,
        // Brawl/Oathbreaker likewise count the command-zone card(s) toward their 60. So size rules
        // check the whole deck (main + command zone), not just the main deck.
        var totalDeckCount = mainDeckCount + commanderCount;
        if (deckType.DeckSizeMin is int min && totalDeckCount < min)
            warnings.Add(new("deck-size-min",
                $"Deck has {totalDeckCount} card{(totalDeckCount == 1 ? "" : "s")}; {deckType.Name} needs at least {min}."));
        if (deckType.DeckSizeMax is int max && totalDeckCount > max)
            warnings.Add(new("deck-size-max",
                $"Deck has {totalDeckCount} card{(totalDeckCount == 1 ? "" : "s")}; {deckType.Name} allows at most {max}."));

        // --- Copy limit / singleton ---
        var copyLimit = deckType.Singleton ? 1 : deckType.MaxCopiesPerCard;
        if (copyLimit is int limit)
        {
            foreach (var (name, info) in mainCopiesByName)
            {
                if (deckType.BasicLandsExempt && info.IsBasicLand) continue;
                if (info.Count > limit)
                    warnings.Add(new("copy-limit",
                        deckType.Singleton
                            ? $"\"{name}\" appears {info.Count}× — {deckType.Name} is singleton (max 1)."
                            : $"\"{name}\" appears {info.Count}× — {deckType.Name} allows at most {limit}."));
            }
        }

        // --- Commander / leader ---
        // Only flag a *missing* commander. Formats like Commander legitimately run more than one
        // command-zone card (partners, backgrounds), so we don't warn on "too many" — and every
        // commander already counts toward the deck-size total above.
        if (deckType.CommanderSlots > 0 && commanderCount == 0)
            warnings.Add(new("commander-count",
                $"{deckType.Name} needs a commander/leader — tag at least one card \"{DeckCardClassifier.CommanderTag}\"."));

        return new DeckLegality
        {
            DeckTypeName = deckType.Name,
            MainDeckCount = mainDeckCount,
            CommanderCount = commanderCount,
            TotalDeckCount = totalDeckCount,
            Warnings = warnings,
        };
    }

    private static (bool IsCommander, bool IsSideboard) ReadReservedTags(IEnumerable<string>? tags)
    {
        if (tags is null) return (false, false);
        bool commander = false, sideboard = false;
        foreach (var t in tags)
        {
            if (string.Equals(t, DeckCardClassifier.CommanderTag, StringComparison.OrdinalIgnoreCase)) commander = true;
            else if (string.Equals(t, DeckCardClassifier.SideboardTag, StringComparison.OrdinalIgnoreCase)) sideboard = true;
        }
        return (commander, sideboard);
    }

    private static bool IsBasicLand(string name, string? cardType)
    {
        if (BasicLandNames.Contains(name)) return true;
        var type = cardType?.ToLowerInvariant() ?? "";
        return type.Contains("basic") && type.Contains("land");
    }
}
