using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public sealed class DeckBoxSyncService(
    IDbContextFactory<OmniCardDbContext> dbContextFactory,
    ICardService cardService,
    ITagService tagService) : IDeckBoxSyncService
{
    public const string SideboardTag = DeckCardClassifier.SideboardTag;

    private static string NormalizeName(string name) => name.Trim().ToLowerInvariant();

    // Candidate keys for a decklist name: the name itself plus the ", " -> " - " fuzzy variant
    // used elsewhere for Riftbound-style names (see DecklistService.CheckAgainstCollection).
    private static IEnumerable<string> NameKeys(string name)
    {
        yield return NormalizeName(name);
        var fuzzy = name.Replace(", ", " - ");
        if (fuzzy != name) yield return NormalizeName(fuzzy);
    }

    private sealed record BoxLot(int LotId, string Name, string? SetCode, bool IsFoil, int Quantity, string? ImageUri, string? ScanImagePath);
    private sealed record SourceLot(int LotId, string Name, int ContainerId, string ContainerName, string? SetCode, string? CollectorNumber, bool IsFoil, int Quantity);

    public DeckBoxSyncPlan BuildPlan(int deckBoxId, List<DecklistEntry> targetEntries, CardGame game)
    {
        using var ctx = dbContextFactory.CreateDbContext();

        var deckBox = ctx.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == deckBoxId);
        var deckBoxName = deckBox?.Name ?? "Deck Box";

        // Current deck box contents (singles, this game), one entry per lot with true quantity.
        var boxLots =
            (from l in ctx.Lots.AsNoTracking()
             join p in ctx.Products.AsNoTracking() on l.ProductId equals p.Id
             where p.Category == ProductCategory.Single && p.Game == game && l.LocationId == deckBoxId
             select new BoxLot(l.Id, p.Name, p.SetCode, p.Foil, l.Quantity, p.ImageUri, l.ScanImagePath))
            .ToList();

        // Everything else in the collection that could supply added copies (must live in a real container
        // other than the deck box so we can name a source location and move it out).
        var sourceLots =
            (from l in ctx.Lots.AsNoTracking()
             join p in ctx.Products.AsNoTracking() on l.ProductId equals p.Id
             join sc in ctx.StorageContainers.AsNoTracking() on l.LocationId equals sc.Id
             where p.Category == ProductCategory.Single && p.Game == game && l.LocationId != deckBoxId
             select new SourceLot(l.Id, p.Name, sc.Id, sc.Name, p.SetCode, p.CollectorNumber, p.Foil, l.Quantity))
            .ToList();

        var boxByName = boxLots
            .GroupBy(b => NormalizeName(b.Name))
            .ToDictionary(g => g.Key, g => g.ToList());
        var sourcesByName = sourceLots
            .GroupBy(s => NormalizeName(s.Name))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Aggregate the target list by name (keep/cut is decided by name — a Sol Ring is a Sol Ring).
        // Retain the first entry's set/collector per name for best-effort printing on the Add row.
        var neededByName = new Dictionary<string, int>();
        var printingByName = new Dictionary<string, DecklistEntry>();
        foreach (var entry in targetEntries)
        {
            var key = NormalizeName(entry.CardName);
            neededByName[key] = neededByName.GetValueOrDefault(key) + entry.Quantity;
            printingByName.TryAdd(key, entry);
        }

        var gameService = cardService.GetGameService(game);
        var adds = new List<DeckBoxAddRow>();
        var keepCount = 0;

        // How many copies of each box name the list keeps (used below to compute surplus cuts).
        var keptByBoxKey = new Dictionary<string, int>();

        foreach (var (nameKey, needed) in neededByName)
        {
            var entry = printingByName[nameKey];

            // Resolve which box group this target name maps to (direct, then fuzzy).
            var boxKey = NameKeys(entry.CardName).FirstOrDefault(boxByName.ContainsKey);
            var inBox = boxKey is null ? 0 : boxByName[boxKey].Sum(b => b.Quantity);

            var keep = Math.Min(needed, inBox);
            keepCount += keep;
            if (boxKey is not null) keptByBoxKey[boxKey] = keep;

            var addQty = needed - keep;
            if (addQty <= 0) continue;

            // Gather candidate source lots (direct then fuzzy name), exact printing first.
            var sourceKey = NameKeys(entry.CardName).FirstOrDefault(sourcesByName.ContainsKey);
            var candidates = sourceKey is null ? new List<SourceLot>() : sourcesByName[sourceKey];
            var sources = candidates
                .Select(s => new DeckBoxAddSource(
                    s.LotId, s.ContainerId, s.ContainerName, s.Quantity, s.SetCode, s.IsFoil,
                    IsExactMatch:
                        (entry.SetCode is null || string.Equals(s.SetCode, entry.SetCode, StringComparison.OrdinalIgnoreCase)) &&
                        (entry.CollectorNumber is null || string.Equals(s.CollectorNumber, entry.CollectorNumber, StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(s => s.IsExactMatch)
                .ThenByDescending(s => s.AvailableQty)
                .ToList();

            var (imageUri, localImagePath) = ResolveImage(gameService, entry);
            adds.Add(new DeckBoxAddRow(entry.CardName, entry.SetCode, entry.CollectorNumber, addQty, sources, imageUri, localImagePath));
        }

        // Cuts: any box lot of a name not in the list, or copies beyond what the list keeps.
        var cuts = new List<DeckBoxCutRow>();
        foreach (var (boxKey, lots) in boxByName)
        {
            var inBox = lots.Sum(b => b.Quantity);
            var kept = keptByBoxKey.GetValueOrDefault(boxKey); // 0 when the name isn't in the list at all
            var surplus = inBox - kept;
            if (surplus <= 0) continue;

            var remaining = surplus;
            foreach (var lot in lots)
            {
                if (remaining <= 0) break;
                var cutQty = Math.Min(lot.Quantity, remaining);
                remaining -= cutQty;
                cuts.Add(new DeckBoxCutRow(lot.LotId, lot.Name, lot.SetCode, lot.IsFoil, cutQty, lot.ImageUri, lot.ScanImagePath));
            }
        }

        return new DeckBoxSyncPlan
        {
            DeckBoxId = deckBoxId,
            DeckBoxName = deckBoxName,
            Cuts = cuts,
            Adds = adds,
            KeepCount = keepCount,
        };
    }

    public void ApplySync(DeckBoxSyncCommitRequest request)
    {
        // Adds: pull exactly the requested quantity from the chosen source lot into the deck box.
        foreach (var add in request.Adds)
        {
            if (add.Quantity < 1) continue;
            cardService.MoveQuantityToContainer(add.SourceLotId, add.Quantity, request.DeckBoxId);
        }

        // Cuts: sideboard = tag in place (no move); otherwise move the cut quantity to the chosen location.
        foreach (var cut in request.Cuts)
        {
            if (cut.Sideboard)
            {
                tagService.AddTagToLots([cut.LotId], SideboardTag);
                continue;
            }
            if (cut.DestinationContainerId is int dest && cut.Quantity >= 1)
                cardService.MoveQuantityToContainer(cut.LotId, cut.Quantity, dest);
        }
    }

    // Best-effort catalog image for an Add row (MTG for v1) — mirrors DecklistService.CheckAgainstCollection.
    private static (string? ImageUri, string? LocalImagePath) ResolveImage(ICardGameService gameService, DecklistEntry entry)
    {
        var printings = DecklistPrintingResolver.GetPrintingsFuzzy(gameService, entry.CardName);
        if (printings.Count == 0) return (null, null);
        var match = entry.SetCode is not null
            ? printings.FirstOrDefault(r => string.Equals(r.SetCode, entry.SetCode, StringComparison.OrdinalIgnoreCase)) ?? printings[0]
            : printings[0];
        if (match.Source is Card card)
            return (card.ImageUris?.Normal ?? card.ImageUris?.Small, card.LocalImagePath);
        return (match.ImageUri, null);
    }
}
