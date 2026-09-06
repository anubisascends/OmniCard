using LinqExpression = System.Linq.Expressions.Expression;
using Microsoft.EntityFrameworkCore;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Collection;

/// <summary>
/// Builds the collection query (the <see cref="CollectionCard"/> projection over Lots/Products/
/// StorageContainers) and the Scryfall-syntax filter that narrows it. Extracted from
/// <see cref="CardService"/> so the read-only web companion's binder editor can run the exact same
/// filtering (e.g. <c>c:u</c>, <c>r&gt;=rare</c>, <c>t:creature</c>, <c>tag:foo</c>) against a
/// writable context without pulling in CardService's WPF/imaging/scanner dependencies.
///
/// Everything here is stateless and static — the only "state" is the <see cref="OmniCardDbContext"/>
/// passed per call (needed by the <c>tag:</c> filter, which resolves matching lot ids via a subquery).
/// </summary>
public static class CollectionQueryBuilder
{
    /// <summary>
    /// Projects Lots→<see cref="CollectionCard"/> (singles only) and applies the optional game /
    /// container / free-text query / <see cref="FilterPreset"/> filters. The returned query is
    /// unmaterialized so callers can add their own <c>.Where</c>/<c>.OrderBy</c> (e.g. the binder
    /// "unplaced pool" adds <c>.Where(c =&gt; c.Page == null)</c>).
    /// </summary>
    public static IQueryable<CollectionCard> BuildFilteredQuery(
        OmniCardDbContext context, string query, CardGame? gameFilter, int? containerFilter, FilterPreset? filterPreset)
    {
        IQueryable<CollectionCard> cards =
            from l in context.Lots.AsNoTracking()
            join p in context.Products.AsNoTracking() on l.ProductId equals p.Id
            where p.Category == ProductCategory.Single
            join sc in context.StorageContainers.AsNoTracking() on l.LocationId equals sc.Id into containerJoin
            from sc in containerJoin.DefaultIfEmpty()
            select new CollectionCard
            {
                Id = l.Id,
                Game = p.Game,
                GameCardId = p.GameCardId ?? "",
                Name = p.Name,
                Quantity = l.Quantity,
                SetName = p.SetName ?? "",
                SetCode = p.SetCode ?? "",
                Number = p.CollectorNumber ?? "",
                Rarity = p.Rarity ?? "",
                ImageUri = p.ImageUri,
                ScanImagePath = l.ScanImagePath,
                Condition = l.Condition ?? "NM",
                IsFoil = p.Foil,
                PurchasePrice = l.UnitCost,
                DateAdded = l.AcquisitionDate,
                ContainerId = l.LocationId,
                Container = sc,
                Page = l.Page,
                Slot = l.Slot,
                Section = l.Section,
                Color = p.Color,
                CardType = p.CardType,
                IsMissing = l.IsMissing,
                FlagReason = l.FlagReason,
                IsTraded = l.IsTraded,
                TradeNote = l.TradeNote,
            };

        if (gameFilter.HasValue)
            cards = cards.Where(c => c.Game == gameFilter.Value);

        if (containerFilter.HasValue)
            cards = cards.Where(c => c.ContainerId == containerFilter.Value);

        if (!string.IsNullOrWhiteSpace(query))
            cards = ApplyScryfallFilter(cards, query, context);

        if (filterPreset is not null && !string.IsNullOrWhiteSpace(filterPreset.Query))
            cards = ApplyScryfallFilter(cards, filterPreset.Query, context);

        return cards;
    }

    private static IQueryable<CollectionCard> ApplyScryfallFilter(IQueryable<CollectionCard> cards, string query, OmniCardDbContext context)
    {
        var filter = ScryfallQueryParser.ParseFilter(query);
        if (filter is null)
            return cards;

        var param = LinqExpression.Parameter(typeof(CollectionCard), "c");
        var expr = BuildFilterExpression(param, filter, context);
        var lambda = LinqExpression.Lambda<Func<CollectionCard, bool>>(expr, param);
        return cards.Where(lambda);
    }

    private static readonly System.Reflection.MethodInfo LikeMethod =
        typeof(DbFunctionsExtensions).GetMethod(
            nameof(DbFunctionsExtensions.Like),
            [typeof(DbFunctions), typeof(string), typeof(string)])!;

    private static LinqExpression CallLike(LinqExpression property, string pattern)
    {
        return LinqExpression.Call(
            LikeMethod,
            LinqExpression.Property(null, typeof(EF), nameof(EF.Functions)),
            property,
            LinqExpression.Constant(pattern));
    }

    private static LinqExpression BuildFilterExpression(System.Linq.Expressions.ParameterExpression param, FilterNode node, OmniCardDbContext context)
    {
        return node switch
        {
            FieldFilter f => BuildFieldExpression(param, f, context),
            AndFilter and => and.Children
                .Select(c => BuildFilterExpression(param, c, context))
                .Aggregate(LinqExpression.AndAlso),
            OrFilter or => or.Children
                .Select(c => BuildFilterExpression(param, c, context))
                .Aggregate(LinqExpression.OrElse),
            NotFilter not => LinqExpression.Not(BuildFilterExpression(param, not.Inner, context)),
            _ => LinqExpression.Constant(true),
        };
    }

    private static LinqExpression BuildFieldExpression(System.Linq.Expressions.ParameterExpression param, FieldFilter filter, OmniCardDbContext context)
    {
        var expr = filter.Field switch
        {
            "name" => BuildNameExpression(param, filter.Op, filter.Value),
            "set" => BuildSetExpression(param, filter.Op, filter.Value),
            "cn" => BuildCnExpression(param, filter.Op, filter.Value),
            "type" => BuildNullableStringExpression(param, nameof(CollectionCard.CardType), filter.Op, filter.Value),
            "rarity" => BuildRarityExpression(param, filter.Op, filter.Value),
            "color" => BuildColorExpression(param, filter.Op, filter.Value),
            "is" => BuildIsExpression(param, filter.Value),
            "foil" => BuildLegacyFoilExpression(param, filter.Value),
            "condition" or "cond" => BuildStringExpression(param, nameof(CollectionCard.Condition), filter.Op, filter.Value),
            "location" or "loc" => BuildLocationExpression(param, filter.Op, filter.Value),
            "tag" => BuildTagExpression(param, context, filter.Op, filter.Value),
            _ => BuildNameExpression(param, filter.Op, filter.Value),
        };

        return filter.Negated ? LinqExpression.Not(expr) : expr;
    }

    /// <summary>Unlike the other field builders, this one isn't pure — it resolves matching lot
    /// ids eagerly via a small subquery against LotTags (there's no scalar "tags" column on
    /// CollectionCard to filter in-place), then bakes the result into the expression tree as a
    /// HashSet.Contains check.</summary>
    private static LinqExpression BuildTagExpression(System.Linq.Expressions.ParameterExpression param, OmniCardDbContext context, ComparisonOp op, string value)
    {
        var matchingLotIds = (op == ComparisonOp.Exact
                ? context.LotTags.Where(lt => lt.Tag.Name.ToLower() == value.ToLower())
                : context.LotTags.Where(lt => EF.Functions.Like(lt.Tag.Name, $"%{value}%")))
            .Select(lt => lt.LotId)
            .Distinct()
            .ToHashSet();

        var containsMethod = typeof(HashSet<int>).GetMethod(nameof(HashSet<int>.Contains), [typeof(int)])!;
        return LinqExpression.Call(
            LinqExpression.Constant(matchingLotIds),
            containsMethod,
            LinqExpression.Property(param, nameof(CollectionCard.Id)));
    }

    private static LinqExpression BuildNameExpression(System.Linq.Expressions.ParameterExpression param, ComparisonOp op, string value)
    {
        var prop = LinqExpression.Property(param, nameof(CollectionCard.Name));
        return op switch
        {
            ComparisonOp.Exact => CallLike(prop, value),
            ComparisonOp.NotEqual => LinqExpression.Not(CallLike(prop, value)),
            _ => CallLike(prop, $"%{value}%"),
        };
    }

    private static LinqExpression BuildSetExpression(System.Linq.Expressions.ParameterExpression param, ComparisonOp op, string value)
    {
        var codeProp = LinqExpression.Property(param, nameof(CollectionCard.SetCode));

        return op switch
        {
            // set:xyz → exact match on set code (case-insensitive via LIKE)
            ComparisonOp.Contains => CallLike(codeProp, value),
            ComparisonOp.Exact => CallLike(codeProp, value),
            ComparisonOp.NotEqual => LinqExpression.Not(CallLike(codeProp, value)),
            _ => CallLike(codeProp, value),
        };
    }

    private static LinqExpression BuildCnExpression(System.Linq.Expressions.ParameterExpression param, ComparisonOp op, string value)
    {
        var prop = LinqExpression.Property(param, nameof(CollectionCard.Number));
        return op switch
        {
            ComparisonOp.NotEqual => LinqExpression.NotEqual(prop, LinqExpression.Constant(value)),
            _ => LinqExpression.Equal(prop, LinqExpression.Constant(value)),
        };
    }

    private static LinqExpression BuildStringExpression(System.Linq.Expressions.ParameterExpression param, string propertyName, ComparisonOp op, string value)
    {
        var prop = LinqExpression.Property(param, propertyName);
        return op switch
        {
            ComparisonOp.Exact => CallLike(prop, value),
            ComparisonOp.NotEqual => LinqExpression.Not(CallLike(prop, value)),
            _ => CallLike(prop, $"%{value}%"),
        };
    }

    private static LinqExpression BuildNullableStringExpression(System.Linq.Expressions.ParameterExpression param, string propertyName, ComparisonOp op, string value)
    {
        var prop = LinqExpression.Property(param, propertyName);
        var notNull = LinqExpression.NotEqual(prop, LinqExpression.Constant(null, typeof(string)));

        if (op == ComparisonOp.NotEqual)
        {
            return LinqExpression.OrElse(
                LinqExpression.Equal(prop, LinqExpression.Constant(null, typeof(string))),
                LinqExpression.Not(CallLike(prop, value)));
        }

        var pattern = op == ComparisonOp.Exact ? value : $"%{value}%";
        return LinqExpression.AndAlso(notNull, CallLike(prop, pattern));
    }

    private static LinqExpression BuildRarityExpression(System.Linq.Expressions.ParameterExpression param, ComparisonOp op, string value)
    {
        var prop = LinqExpression.Property(param, nameof(CollectionCard.Rarity));

        if (op == ComparisonOp.Contains || op == ComparisonOp.Exact)
            return CallLike(prop, value);

        var matching = ScryfallQueryParser.RaritiesMatching(op, value);
        if (matching.Count == 0)
            return LinqExpression.Constant(false);

        return matching
            .Select(r => CallLike(prop, r))
            .Aggregate(LinqExpression.OrElse);
    }

    private static LinqExpression BuildColorExpression(System.Linq.Expressions.ParameterExpression param, ComparisonOp op, string value)
    {
        var prop = LinqExpression.Property(param, nameof(CollectionCard.Color));
        var notNull = LinqExpression.NotEqual(prop, LinqExpression.Constant(null, typeof(string)));

        // ExtractColor stores the literal buckets "Colorless"/"Land" for cards with no
        // WUBRG colors, rather than null/empty - see CardAttributeExtractor.ExtractMtgColor.
        var isColorlessBucket = LinqExpression.OrElse(
            LinqExpression.Equal(prop, LinqExpression.Constant(null, typeof(string))),
            LinqExpression.OrElse(
                LinqExpression.Equal(prop, LinqExpression.Constant("")),
                LinqExpression.OrElse(
                    CallLike(prop, "Colorless"),
                    CallLike(prop, "Land"))));

        // colorless
        if (value.Equals("colorless", StringComparison.OrdinalIgnoreCase) || value.Equals("c", StringComparison.OrdinalIgnoreCase))
        {
            return op == ComparisonOp.NotEqual ? LinqExpression.Not(isColorlessBucket) : isColorlessBucket;
        }

        // multicolor: Color has 2+ characters and isn't a colorless/land bucket
        if (value.Equals("multicolor", StringComparison.OrdinalIgnoreCase) || value.Equals("multi", StringComparison.OrdinalIgnoreCase))
        {
            var lengthProp = LinqExpression.Property(prop, nameof(string.Length));
            var isMulti = LinqExpression.AndAlso(
                LinqExpression.AndAlso(notNull, LinqExpression.Not(isColorlessBucket)),
                LinqExpression.GreaterThanOrEqual(lengthProp, LinqExpression.Constant(2)));
            return op == ComparisonOp.NotEqual ? LinqExpression.Not(isMulti) : isMulti;
        }

        var normalized = ScryfallQueryParser.NormalizeColorValue(value);
        if (normalized.Length == 0)
            return LinqExpression.Constant(true);

        // Exclude the colorless/land buckets from letter-based matching so, e.g.,
        // c:r doesn't match a "Colorless" card just because that word contains an 'r'.
        var notColorlessBucket = LinqExpression.AndAlso(notNull, LinqExpression.Not(isColorlessBucket));

        return op switch
        {
            // : and >= mean "includes at least these colors"
            ComparisonOp.Contains or ComparisonOp.GreaterOrEqual => BuildColorSuperset(prop, notColorlessBucket, normalized),
            // = means "exactly these colors"
            ComparisonOp.Exact => LinqExpression.AndAlso(notColorlessBucket, CallLike(prop, normalized)),
            // != means "not exactly these colors"
            ComparisonOp.NotEqual => LinqExpression.OrElse(
                LinqExpression.Equal(prop, LinqExpression.Constant(null, typeof(string))),
                LinqExpression.OrElse(isColorlessBucket, LinqExpression.Not(CallLike(prop, normalized)))),
            // <= means "at most these colors" (subset)
            ComparisonOp.LessOrEqual => BuildColorSubset(prop, notColorlessBucket, normalized),
            // < means "strict subset"
            ComparisonOp.LessThan => LinqExpression.AndAlso(
                BuildColorSubset(prop, notColorlessBucket, normalized),
                LinqExpression.Not(CallLike(prop, normalized))),
            // > means "strict superset"
            ComparisonOp.GreaterThan => LinqExpression.AndAlso(
                BuildColorSuperset(prop, notColorlessBucket, normalized),
                LinqExpression.Not(CallLike(prop, normalized))),
            _ => BuildColorSuperset(prop, notColorlessBucket, normalized),
        };
    }

    /// <summary>Card's colors include all of the specified colors (superset).</summary>
    private static LinqExpression BuildColorSuperset(LinqExpression prop, LinqExpression notNull, string colors)
    {
        LinqExpression expr = notNull;
        foreach (var c in colors)
            expr = LinqExpression.AndAlso(expr, CallLike(prop, $"%{c}%"));
        return expr;
    }

    /// <summary>Card's colors don't include any color NOT in the specified set (subset).</summary>
    private static LinqExpression BuildColorSubset(LinqExpression prop, LinqExpression notNull, string colors)
    {
        const string allColors = "WUBRG";
        LinqExpression expr = notNull;
        foreach (var c in allColors.Where(c => !colors.Contains(c)))
            expr = LinqExpression.AndAlso(expr, LinqExpression.Not(CallLike(prop, $"%{c}%")));
        return expr;
    }

    private static LinqExpression BuildIsExpression(System.Linq.Expressions.ParameterExpression param, string value)
    {
        return value.ToLowerInvariant() switch
        {
            "foil" => LinqExpression.Equal(
                LinqExpression.Property(param, nameof(CollectionCard.IsFoil)),
                LinqExpression.Constant(true)),
            "missing" => LinqExpression.Equal(
                LinqExpression.Property(param, nameof(CollectionCard.IsMissing)),
                LinqExpression.Constant(true)),
            "missingdb" => LinqExpression.Equal(
                LinqExpression.Property(param, nameof(CollectionCard.FlagReason)),
                LinqExpression.Constant((FlagReason?)FlagReason.MissingFromDatabase, typeof(FlagReason?))),
            _ => LinqExpression.Constant(true),
        };
    }

    private static LinqExpression BuildLegacyFoilExpression(System.Linq.Expressions.ParameterExpression param, string value)
    {
        var isFoil = value.Equals("true", StringComparison.OrdinalIgnoreCase)
                  || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                  || value == "1";
        return LinqExpression.Equal(
            LinqExpression.Property(param, nameof(CollectionCard.IsFoil)),
            LinqExpression.Constant(isFoil));
    }

    private static LinqExpression BuildLocationExpression(System.Linq.Expressions.ParameterExpression param, ComparisonOp op, string value)
    {
        var containerProp = LinqExpression.Property(param, nameof(CollectionCard.Container));
        var notNull = LinqExpression.NotEqual(containerProp, LinqExpression.Constant(null, typeof(StorageContainer)));
        var nameProp = LinqExpression.Property(containerProp, nameof(StorageContainer.Name));

        if (op == ComparisonOp.NotEqual)
        {
            return LinqExpression.OrElse(
                LinqExpression.Equal(containerProp, LinqExpression.Constant(null, typeof(StorageContainer))),
                LinqExpression.Not(CallLike(nameProp, value)));
        }

        var pattern = op == ComparisonOp.Exact ? value : $"%{value}%";
        return LinqExpression.AndAlso(notNull, CallLike(nameProp, pattern));
    }
}
