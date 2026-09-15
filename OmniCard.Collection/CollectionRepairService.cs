using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Settings;

namespace OmniCard.Collection;

/// <summary>
/// One-time repair of MTG single <see cref="Product"/> rows in the unified store, for collections
/// built before imports resolved denormalized attributes. It:
/// <list type="number">
///   <item>merges any genuine duplicate products — same (Game, case-insensitive SetCode,
///   CollectorNumber, Foil) — reassigning their lots/movements/order-lines to one canonical row;</item>
///   <item>backfills null/empty <see cref="Product.Color"/>/<see cref="Product.CardType"/> from the
///   Scryfall catalog (looked up by set code + collector number), so imported cards display colour
///   exactly like scanned ones; and</item>
///   <item>normalises SetCode to Scryfall's lowercase form so filtering/grouping by set is stable.</item>
/// </list>
/// Guarded by a <see cref="MigrationState"/> DB marker so it runs at most once per collection, and
/// skipped entirely until the Scryfall catalog has been downloaded (so a fresh install doesn't burn
/// the one-shot before there's any data to repair against).
/// </summary>
public sealed class CollectionRepairService(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    IScryfallService scryfall,
    ILogger<CollectionRepairService> logger)
{
    /// <summary>Key of the <see cref="MigrationState"/> row that marks the colour/dedup repair complete.</summary>
    public const string MigrationStateKey = "MtgColorBackfillDedup";

    /// <summary>Key marking the one-time rewrite of collapsed MTG <see cref="Product.CardType"/> buckets
    /// to the full catalog type line (so <c>t:vampire</c>/<c>t:legendary</c> match owned cards).</summary>
    public const string FullTypeLineStateKey = "MtgFullTypeLineBackfill";

    /// <summary>
    /// Runs the outstanding one-time repairs. Each is guarded by its own <see cref="MigrationState"/>
    /// marker, so a collection that already ran the original colour/dedup pass still picks up newer
    /// repairs (e.g. the full-type-line rewrite). No-op (returns 0) while the Scryfall catalog is empty
    /// (nothing to repair against — retried on a later launch once data is downloaded). Returns the total
    /// number of products changed.
    /// </summary>
    public int RepairIfNeeded()
    {
        using var ctx = dbFactory.CreateDbContext();

        if (!scryfall.Cards.Any())
        {
            logger.LogInformation("Skipping MTG collection repair: Scryfall catalog is empty (will retry after a data download)");
            return 0;
        }

        var changed = 0;

        if (!ctx.MigrationState.AsNoTracking().Any(m => m.Key == MigrationStateKey))
        {
            var merged = MergeDuplicateProducts(ctx);
            var backfilled = BackfillColorsAndSetCodes(ctx);
            ctx.MigrationState.Add(new MigrationState { Key = MigrationStateKey, CompletedAt = DateTime.UtcNow });
            ctx.SaveChanges();
            logger.LogInformation("MTG colour repair complete: {Merged} duplicate products merged, {Backfilled} products backfilled/re-cased",
                merged, backfilled);
            changed += merged + backfilled;
        }

        changed += RepairCardTypesIfNeeded();

        return changed;
    }

    /// <summary>
    /// Runs only the one-time full-type-line rewrite (guarded by <see cref="FullTypeLineStateKey"/>).
    /// Split out from <see cref="RepairIfNeeded"/> so the web host can run just this pass without also
    /// triggering the duplicate-merge/colour backfill — those were desktop-era repairs that group by
    /// (set, number, foil) and would be unsafe to run for the first time on a live web collection.
    /// No-op (returns 0) while the Scryfall catalog is empty. Returns the number of products retyped.
    /// </summary>
    public int RepairCardTypesIfNeeded()
    {
        using var ctx = dbFactory.CreateDbContext();

        if (ctx.MigrationState.AsNoTracking().Any(m => m.Key == FullTypeLineStateKey))
            return 0;

        if (!scryfall.Cards.Any())
        {
            logger.LogInformation("Skipping MTG type-line repair: Scryfall catalog is empty (will retry after a data download)");
            return 0;
        }

        var retyped = RefreshMtgCardTypes(ctx);
        ctx.MigrationState.Add(new MigrationState { Key = FullTypeLineStateKey, CompletedAt = DateTime.UtcNow });
        ctx.SaveChanges();
        logger.LogInformation("MTG type-line repair complete: {Retyped} product CardType(s) rewritten to the full type line", retyped);
        return retyped;
    }

    /// <summary>
    /// Rewrites the denormalised <see cref="Product.CardType"/> of every MTG single to the catalog's full
    /// type line (looked up by GameCardId, falling back to set code + collector number). Earlier builds
    /// stored a collapsed bucket ("Legendary Creature", "Artifact") that dropped subtypes/supertypes, so
    /// this backfill is what makes <c>t:vampire</c>/<c>t:saga</c>/<c>t:legendary</c> work on pre-existing
    /// collections. Rows that don't resolve to a catalog card are left untouched.
    /// </summary>
    private int RefreshMtgCardTypes(OmniCardDbContext ctx)
    {
        var products = ctx.Products
            .Where(p => p.Game == CardGame.Mtg && p.Category == ProductCategory.Single)
            .ToList();

        // Look up each distinct (set, number) once; the GameCardId fallback handles odd numbers/promos.
        var wanted = products
            .Where(p => !string.IsNullOrEmpty(p.SetCode) && !string.IsNullOrEmpty(p.CollectorNumber))
            .Select(p => (Set: p.SetCode!.ToLowerInvariant(), Number: p.CollectorNumber!))
            .Distinct()
            .ToList();

        var catalog = new Dictionary<(string Set, string Number), string>();
        foreach (var (set, number) in wanted)
        {
            var typeLine = scryfall.Cards.AsNoTracking()
                .Where(c => c.SetCode == set && c.CollectorNumber == number && c.Lang == "en")
                .Select(c => c.TypeLine)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(typeLine))
                catalog[(set, number)] = typeLine;
        }

        var changed = 0;
        foreach (var product in products)
        {
            string? typeLine = null;
            if (!string.IsNullOrEmpty(product.SetCode) && !string.IsNullOrEmpty(product.CollectorNumber))
                catalog.TryGetValue((product.SetCode!.ToLowerInvariant(), product.CollectorNumber!), out typeLine);

            if (string.IsNullOrEmpty(typeLine) && Guid.TryParse(product.GameCardId, out var id))
                typeLine = scryfall.Cards.AsNoTracking().Where(c => c.Id == id).Select(c => c.TypeLine).FirstOrDefault();

            if (!string.IsNullOrEmpty(typeLine) && product.CardType != typeLine)
            {
                product.CardType = typeLine;
                changed++;
            }
        }

        ctx.SaveChanges();
        return changed;
    }

    /// <summary>
    /// Merges products that describe the same physical printing but exist as separate rows — same
    /// game, set (case-insensitive), collector number and foil state, differing only by GameCardId
    /// (e.g. a stale id left behind after a Scryfall re-import). Lots, movements and order-lines are
    /// repointed to a single canonical product (preferring one that already has colour, then the one
    /// backing the most lots) and the orphans are deleted.
    /// </summary>
    private int MergeDuplicateProducts(OmniCardDbContext ctx)
    {
        var singles = ctx.Products
            .Where(p => p.Game == CardGame.Mtg && p.Category == ProductCategory.Single)
            .ToList();

        var groups = singles
            .GroupBy(p => (Set: p.SetCode?.ToLowerInvariant() ?? "", p.CollectorNumber, p.Foil))
            .Where(g => g.Count() > 1)
            .ToList();

        if (groups.Count == 0)
            return 0;

        var lotCounts = ctx.Lots
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .ToDictionary(x => x.ProductId, x => x.Count);

        var merged = 0;
        foreach (var group in groups)
        {
            var canonical = group
                .OrderByDescending(p => string.IsNullOrEmpty(p.Color) ? 0 : 1)
                .ThenByDescending(p => lotCounts.GetValueOrDefault(p.Id, 0))
                .ThenBy(p => p.Id)
                .First();

            foreach (var orphan in group.Where(p => p.Id != canonical.Id))
            {
                foreach (var lot in ctx.Lots.Where(l => l.ProductId == orphan.Id))
                    lot.ProductId = canonical.Id;
                foreach (var mv in ctx.Movements.Where(m => m.ProductId == orphan.Id))
                    mv.ProductId = canonical.Id;
                foreach (var ol in ctx.OrderLines.Where(o => o.ProductId == orphan.Id))
                    ol.ProductId = canonical.Id;

                ctx.Products.Remove(orphan);
                merged++;
            }
        }

        ctx.SaveChanges();
        logger.LogInformation("Merged {Count} duplicate MTG product(s) across {Groups} printing(s)", merged, groups.Count);
        return merged;
    }

    /// <summary>
    /// Backfills missing Color/CardType and normalises SetCode casing for MTG single products, using
    /// the Scryfall catalog looked up by (lowercased SetCode, CollectorNumber, English). Existing
    /// non-empty colour/type values are never overwritten.
    /// </summary>
    private int BackfillColorsAndSetCodes(OmniCardDbContext ctx)
    {
        var products = ctx.Products
            .Where(p => p.Game == CardGame.Mtg && p.Category == ProductCategory.Single)
            .ToList();

        static bool NeedsCatalog(Product p) =>
            string.IsNullOrEmpty(p.Color) || string.IsNullOrEmpty(p.CardType) || string.IsNullOrEmpty(p.SetName);

        // Only the catalog-hungry rows warrant a lookup; a row that just needs its SetCode re-cased is
        // handled by the cheap lowercase branch below. Look up once per distinct (set, number) pair.
        var wanted = products
            .Where(p => NeedsCatalog(p) && !string.IsNullOrEmpty(p.SetCode) && !string.IsNullOrEmpty(p.CollectorNumber))
            .Select(p => (Set: p.SetCode!.ToLowerInvariant(), Number: p.CollectorNumber!))
            .Distinct()
            .ToList();

        var catalog = new Dictionary<(string Set, string Number), Card>();
        foreach (var (set, number) in wanted)
        {
            var card = scryfall.Cards.AsNoTracking()
                .FirstOrDefault(c => c.SetCode == set && c.CollectorNumber == number && c.Lang == "en");
            if (card is not null)
                catalog[(set, number)] = card;
        }

        var changed = 0;
        foreach (var product in products)
        {
            var before = (product.Color, product.CardType, product.SetCode, product.SetName);

            Card? card = null;
            if (NeedsCatalog(product) && !string.IsNullOrEmpty(product.SetCode) && !string.IsNullOrEmpty(product.CollectorNumber))
                catalog.TryGetValue((product.SetCode!.ToLowerInvariant(), product.CollectorNumber!), out card);

            // Fallback to GameCardId when set+number didn't resolve (e.g. a promo with an odd number).
            if (card is null && NeedsCatalog(product) && Guid.TryParse(product.GameCardId, out var id))
                card = scryfall.Cards.AsNoTracking().FirstOrDefault(c => c.Id == id);

            if (card is not null)
            {
                var match = new CardMatch { Source = card };
                if (string.IsNullOrEmpty(product.Color))
                    product.Color = CardAttributeExtractor.ExtractColor(match, CardGame.Mtg);
                if (string.IsNullOrEmpty(product.CardType))
                    product.CardType = CardAttributeExtractor.ExtractCardType(match, CardGame.Mtg);
                if (string.IsNullOrEmpty(product.SetName))
                    product.SetName = card.SetName;
                // Adopt the catalog's authoritative (lowercase) set code.
                product.SetCode = card.SetCode;
            }
            else if (!string.IsNullOrEmpty(product.SetCode))
            {
                // Even unresolved rows get a consistent lowercase set code so grouping/filtering is stable.
                product.SetCode = product.SetCode!.ToLowerInvariant();
            }

            if (before != (product.Color, product.CardType, product.SetCode, product.SetName))
                changed++;
        }

        ctx.SaveChanges();
        logger.LogInformation("Backfilled/normalised {Count} MTG single product(s)", changed);
        return changed;
    }
}
