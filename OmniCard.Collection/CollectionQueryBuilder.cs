using LinqExpression = System.Linq.Expressions.Expression;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Scanning;
using OmniCard.Shared.Storage;
using OmniCard.CardMatching.Search;

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
    /// <para><paramref name="siteIds"/> restricts the result to cards whose location sits in one of
    /// those <see cref="Shared.Sites.Site"/>s (cards with no location count as the default site);
    /// null = every site. Callers pass the signed-in user's readable sites so a search never reveals
    /// cards in a site they can't see.</para>
    /// </summary>
    public static IQueryable<CollectionCard> BuildFilteredQuery(
        OmniCardDbContext context, string query, CardGame? gameFilter, int? containerFilter, FilterPreset? filterPreset,
        IReadOnlyDictionary<CardGame, ICardGameService>? gameServices = null,
        IReadOnlyCollection<int>? siteIds = null)
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
                Language = l.Language ?? "en",
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

        if (siteIds is not null)
            cards = ApplySiteFilter(cards, context, siteIds);

        // Resolve the active game's field schema so game-specific aliases (e.g. FFTCG e:→element)
        // parse correctly. Null when no single game is selected or no resolver is wired — the parser
        // then uses the shared default aliases and game-specific fields still resolve by full name.
        SearchSchema? schema = gameFilter.HasValue && gameServices is not null
            && gameServices.TryGetValue(gameFilter.Value, out var activeSvc) && activeSvc is IGameFieldResolver r
            ? r.SearchSchema : null;

        if (!string.IsNullOrWhiteSpace(query))
            cards = ApplyScryfallFilter(cards, query, context, gameFilter, gameServices, schema);

        if (filterPreset is not null && !string.IsNullOrWhiteSpace(filterPreset.Query))
            cards = ApplyScryfallFilter(cards, filterPreset.Query, context, gameFilter, gameServices, schema);

        return cards;
    }

    /// <summary>Keeps only cards located in one of <paramref name="siteIds"/>; unlocated cards are
    /// treated as the default site. Translates to an <c>IN (subquery)</c> over StorageContainers.</summary>
    public static IQueryable<CollectionCard> ApplySiteFilter(
        IQueryable<CollectionCard> cards, OmniCardDbContext context, IReadOnlyCollection<int> siteIds)
    {
        var ids = siteIds.Distinct().ToList();
        var includeUnlocated = ids.Contains(Shared.Sites.Site.DefaultSiteId);
        var visibleContainerIds = context.StorageContainers.AsNoTracking()
            .Where(sc => ids.Contains(sc.SiteId))
            .Select(sc => sc.Id);
        return cards.Where(c =>
            (includeUnlocated && c.ContainerId == null)
            || (c.ContainerId != null && visibleContainerIds.Contains(c.ContainerId.Value)));
    }

    private static IQueryable<CollectionCard> ApplyScryfallFilter(
        IQueryable<CollectionCard> cards, string query, OmniCardDbContext context,
        CardGame? gameFilter, IReadOnlyDictionary<CardGame, ICardGameService>? gameServices, SearchSchema? schema)
    {
        var filter = ScryfallQueryParser.ParseFilter(query, schema);
        if (filter is null)
            return cards;

        if (gameServices is not null && HasCatalogFieldUnderOrOrNot(filter, gameFilter, gameServices, under: false))
            return ApplyInMemory(cards, filter, context, gameServices);

        var param = LinqExpression.Parameter(typeof(CollectionCard), "c");
        var expr = BuildFilterExpression(param, filter, context, gameFilter, gameServices);
        var lambda = LinqExpression.Lambda<Func<CollectionCard, bool>>(expr, param);
        return cards.Where(lambda);
    }

    /// <summary>
    /// A catalog field (element:, kw:, f:modern, is:commander, …) filters by a list of matching printing
    /// ids. ANDed, that list is a cheap semi-join; under OR or NOT, SQL Server re-reads the JSON list for
    /// every row (<c>-f:modern</c> over ~47k ids ran past the 30 s timeout). Such queries are evaluated
    /// here instead: load the already game/location/site-filtered cards, match them with
    /// <see cref="CollectionCardMatcher"/> (field semantics kept in lockstep with the SQL builders; its
    /// catalog lookups are restricted to these printings), and narrow the query to the matching lot ids —
    /// one ANDed list, so paging, sorting and counting stay in SQL.
    /// </summary>
    private static IQueryable<CollectionCard> ApplyInMemory(IQueryable<CollectionCard> cards, FilterNode filter,
        OmniCardDbContext context, IReadOnlyDictionary<CardGame, ICardGameService> gameServices)
    {
        var loaded = cards.ToList();
        if (UsesField(filter, "tag"))
        {
            var lotIds = loaded.Select(c => c.Id).ToList();
            var tagsByLot = context.LotTags.AsNoTracking()
                .Where(lt => EF.Parameter(lotIds).Contains(lt.LotId))
                .Select(lt => new { lt.LotId, lt.Tag.Name })
                .AsEnumerable()
                .GroupBy(x => x.LotId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());
            foreach (var card in loaded)
                card.Tags = tagsByLot.GetValueOrDefault(card.Id) ?? [];
        }

        var resolve = CollectionCardMatcher.CreateResolver(gameServices, loaded);
        var matching = loaded.Where(c => CollectionCardMatcher.Matches(c, filter, resolve)).Select(c => c.Id).ToList();
        return cards.Where(c => EF.Parameter(matching).Contains(c.Id));
    }

    /// <summary>The ownership <c>is:</c> flags, read from the lot (see <see cref="BuildIsExpression"/>).</summary>
    private static readonly HashSet<string> OwnedIsFlags = new(StringComparer.OrdinalIgnoreCase)
        { "foil", "nonfoil", "missing", "missingdb" };

    /// <summary>Whether <paramref name="f"/> is answered from the game catalog rather than the collection.</summary>
    private static bool IsCatalogField(FieldFilter f, CardGame? gameFilter, IReadOnlyDictionary<CardGame, ICardGameService> gameServices)
    {
        if (f.Field == "is") return !OwnedIsFlags.Contains(f.Value);
        if (CollectionFields.Contains(f.Field)) return false;
        IEnumerable<ICardGameService> inScope = gameFilter is { } g
            ? gameServices.TryGetValue(g, out var one) ? [one] : []
            : gameServices.Values;
        return inScope.Any(svc => svc is IGameFieldResolver r && r.SearchSchema.IsGameSpecific(f.Field));
    }

    private static bool HasCatalogFieldUnderOrOrNot(FilterNode node, CardGame? gameFilter,
        IReadOnlyDictionary<CardGame, ICardGameService> gameServices, bool under) => node switch
    {
        FieldFilter f => (under || f.Negated) && IsCatalogField(f, gameFilter, gameServices),
        AndFilter a => a.Children.Any(c => HasCatalogFieldUnderOrOrNot(c, gameFilter, gameServices, under)),
        OrFilter o => o.Children.Any(c => HasCatalogFieldUnderOrOrNot(c, gameFilter, gameServices, under: true)),
        NotFilter n => HasCatalogFieldUnderOrOrNot(n.Inner, gameFilter, gameServices, under: true),
        _ => false,
    };

    private static bool UsesField(FilterNode node, string field) => node switch
    {
        FieldFilter f => f.Field == field,
        AndFilter a => a.Children.Any(c => UsesField(c, field)),
        OrFilter o => o.Children.Any(c => UsesField(c, field)),
        NotFilter n => UsesField(n.Inner, field),
        _ => false,
    };

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

    private static LinqExpression BuildFilterExpression(System.Linq.Expressions.ParameterExpression param, FilterNode node, OmniCardDbContext context,
        CardGame? gameFilter, IReadOnlyDictionary<CardGame, ICardGameService>? gameServices)
    {
        return node switch
        {
            FieldFilter f => BuildFieldExpression(param, f, context, gameFilter, gameServices),
            AndFilter and => and.Children
                .Select(c => BuildFilterExpression(param, c, context, gameFilter, gameServices))
                .Aggregate(LinqExpression.AndAlso),
            OrFilter or => or.Children
                .Select(c => BuildFilterExpression(param, c, context, gameFilter, gameServices))
                .Aggregate(LinqExpression.OrElse),
            NotFilter not => LinqExpression.Not(BuildFilterExpression(param, not.Inner, context, gameFilter, gameServices)),
            _ => LinqExpression.Constant(true),
        };
    }

    /// <summary>Fields built from the collection's own columns (or tags); anything else is offered to the
    /// games' field resolvers first.</summary>
    private static readonly HashSet<string> CollectionFields =
    [
        "name", "set", "cn", "type", "rarity", "color", "foil", "condition", "cond", "lang", "language",
        "location", "loc", "tag",
    ];

    private static LinqExpression BuildFieldExpression(System.Linq.Expressions.ParameterExpression param, FieldFilter filter, OmniCardDbContext context,
        CardGame? gameFilter, IReadOnlyDictionary<CardGame, ICardGameService>? gameServices)
    {
        // Catalog fields (element:, kw:, f:modern, is:commander, …) rather than the collection's own. An is:
        // flag that's neither an ownership flag nor recognized by a game matches nothing.
        if (filter.Field == "is" ? !OwnedIsFlags.Contains(filter.Value) : !CollectionFields.Contains(filter.Field))
        {
            if (BuildGameFieldExpression(param, filter, context, gameFilter, gameServices) is { } gameField)
                return filter.Negated ? LinqExpression.Not(gameField) : gameField;
            if (filter.Field == "is")
                return LinqExpression.Constant(filter.Negated);
        }

        var expr = filter.Field switch
        {
            "name" => BuildNameExpression(param, filter.Op, filter.Value),
            "set" => BuildSetExpression(param, filter.Op, filter.Value),
            "cn" => BuildCnExpression(param, filter.Op, filter.Value),
            "type" => BuildNullableStringExpression(param, nameof(CollectionCard.CardType), filter.Op, filter.Value),
            "rarity" => BuildRarityExpression(param, filter.Op, filter.Value),
            "color" => BuildColorExpression(param, filter.Op, filter.Value),
            "is" => BuildIsExpression(param, filter.Value)!, // an ownership flag (catalog flags returned above)
            "foil" => BuildLegacyFoilExpression(param, filter.Value),
            "condition" or "cond" => BuildStringExpression(param, nameof(CollectionCard.Condition), filter.Op, filter.Value),
            "lang" or "language" => BuildLanguageExpression(param, filter.Op, filter.Value),
            "location" or "loc" => BuildLocationExpression(param, filter.Op, filter.Value),
            "tag" => BuildTagExpression(param, context, filter.Op, filter.Value),
            // A field no game recognized (tried above) falls back to a name search.
            _ => BuildNameExpression(param, filter.Op, filter.Value),
        };

        return filter.Negated ? LinqExpression.Not(expr) : expr;
    }

    /// <summary><c>EF.Parameter(values).Contains(member)</c>: the values travel as ONE JSON parameter
    /// (OPENJSON on SQL Server, json_each on SQLite). Baking the set in as a constant instead inlines every
    /// value into the SQL text — a broad MTG field (f:modern, ~47k owned printings) took ~20 s per query.</summary>
    private static LinqExpression ParameterContains<T>(IEnumerable<T> values, LinqExpression member)
    {
        var parameterized = LinqExpression.Call(typeof(EF), nameof(EF.Parameter), [typeof(T[])],
            LinqExpression.Constant(values.ToArray()));
        return LinqExpression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(T)], parameterized, member);
    }

    /// <summary>Resolves a game-specific field to the set of matching catalog GameCardIds via the game
    /// service (crossing the owned-store ↔ catalog DB boundary), then bakes a
    /// <c>Contains(c.GameCardId)</c> check over a parameterized id list — the same trick as
    /// <see cref="BuildTagExpression"/>. Only owned printings can match, so they're passed as the
    /// candidate set (lets MTG evaluate in-memory-only fields without scanning the whole catalog). Only
    /// used for ANDed, un-negated fields — anything else goes through <see cref="ApplyInMemory"/>.
    /// Returns null when no wired game recognizes the field.</summary>
    private static LinqExpression? BuildGameFieldExpression(System.Linq.Expressions.ParameterExpression param, FieldFilter filter,
        OmniCardDbContext context, CardGame? gameFilter, IReadOnlyDictionary<CardGame, ICardGameService>? gameServices)
    {
        if (gameServices is null) return null;

        CardGame[] games = gameFilter.HasValue ? [gameFilter.Value] : gameServices.Keys.ToArray();
        var clauses = new List<LinqExpression>();
        bool recognized = false;

        foreach (var g in games)
        {
            if (!gameServices.TryGetValue(g, out var svc) || svc is not IGameFieldResolver resolver) continue;
            var owned = context.Products.AsNoTracking()
                .Where(p => p.Game == g && p.Category == ProductCategory.Single && p.GameCardId != null)
                .Select(p => p.GameCardId!).Distinct().ToList();
            var ids = resolver.ResolveFieldCardIds(filter.Field, filter.Op, filter.Value, owned);
            if (ids is null) continue; // this game doesn't define the field

            recognized = true;
            var contains = ParameterContains(ids, LinqExpression.Property(param, nameof(CollectionCard.GameCardId)));

            // Under "All Games", guard each game's id-set by its Game so ids can't cross-match.
            clauses.Add(gameFilter.HasValue
                ? contains
                : LinqExpression.AndAlso(
                    LinqExpression.Equal(LinqExpression.Property(param, nameof(CollectionCard.Game)), LinqExpression.Constant(g)),
                    contains));
        }

        if (!recognized) return null;
        return clauses.Count == 0 ? LinqExpression.Constant(false) : clauses.Aggregate(LinqExpression.OrElse);
    }

    /// <summary>Unlike the other field builders, this one isn't pure — it resolves matching lot
    /// ids eagerly via a small subquery against LotTags (there's no scalar "tags" column on
    /// CollectionCard to filter in-place), then bakes the result into the expression tree as a
    /// Contains check over a parameterized id list.</summary>
    private static LinqExpression BuildTagExpression(System.Linq.Expressions.ParameterExpression param, OmniCardDbContext context, ComparisonOp op, string value)
    {
        var matchingLotIds = (op == ComparisonOp.Exact
                ? context.LotTags.Where(lt => lt.Tag.Name.ToLower() == value.ToLower())
                : context.LotTags.Where(lt => EF.Functions.Like(lt.Tag.Name, $"%{value}%")))
            .Select(lt => lt.LotId)
            .Distinct()
            .ToList();

        return ParameterContains(matchingLotIds, LinqExpression.Property(param, nameof(CollectionCard.Id)));
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

    // lang:ja — exact match on the copy's language code. The value is normalized first so printed/ISO
    // spellings work too (lang:jp, lang:japanese); an unrecognized value falls back to a literal match.
    private static LinqExpression BuildLanguageExpression(System.Linq.Expressions.ParameterExpression param, ComparisonOp op, string value)
    {
        var prop = LinqExpression.Property(param, nameof(CollectionCard.Language));
        var code = CardLanguages.Normalize(value) ?? value.Trim().ToLowerInvariant();
        var equal = LinqExpression.Equal(prop, LinqExpression.Constant(code));
        return op == ComparisonOp.NotEqual ? LinqExpression.Not(equal) : equal;
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

    /// <summary>The ownership <c>is:</c> flags, read from the lot. Null for any other value (a catalog
    /// flag the caller hands to the game resolver).</summary>
    private static LinqExpression? BuildIsExpression(System.Linq.Expressions.ParameterExpression param, string value)
    {
        return value.ToLowerInvariant() switch
        {
            "foil" => LinqExpression.Equal(
                LinqExpression.Property(param, nameof(CollectionCard.IsFoil)),
                LinqExpression.Constant(true)),
            "nonfoil" => LinqExpression.Equal(
                LinqExpression.Property(param, nameof(CollectionCard.IsFoil)),
                LinqExpression.Constant(false)),
            "missing" => LinqExpression.Equal(
                LinqExpression.Property(param, nameof(CollectionCard.IsMissing)),
                LinqExpression.Constant(true)),
            "missingdb" => LinqExpression.Equal(
                LinqExpression.Property(param, nameof(CollectionCard.FlagReason)),
                LinqExpression.Constant((FlagReason?)FlagReason.MissingFromDatabase, typeof(FlagReason?))),
            _ => null,
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
