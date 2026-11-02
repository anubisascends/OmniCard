using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Sales;
using OmniCard.Web.Api.Controllers;
using OmniCard.Web.Api.Mapping;
using OmniCard.Web.Helpers;

namespace OmniCard.Web.Services.Dashboard;

/// <summary>
/// Works out a Dashboard widget's data as a table (<see cref="DashboardDataDto"/>), whatever it will be
/// drawn as. Widgets never follow the game picked in the top bar: a card search names its own game (or
/// none, for every game).
/// <list type="bullet">
/// <item><c>metric</c> / <c>breakdown</c> — the holdings valuation and realized sales already behind the
/// classic Dashboard (every site, as before), cached for <see cref="AnalyticsTtl"/> so a page of
/// widgets computes them once.</item>
/// <item><c>search</c> — owned singles (not traded away) matching the widget's Scryfall-style search,
/// limited to <c>siteIds</c>.</item>
/// <item><c>sql</c> — <see cref="DashboardSqlRunner"/>. Who may run it is the caller's check.</item>
/// </list>
/// Column keys of the built-in sources are stable names the page translates.
/// </summary>
public sealed class DashboardDataService(
    IAnalyticsService analytics,
    IDbContextFactory<OmniCardDbContext> dbFactory,
    ICardService cardService,
    IEnumerable<ICardGameService> gameServices,
    DashboardSqlRunner sql,
    TimeProvider time)
{
    public static readonly TimeSpan AnalyticsTtl = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyDictionary<CardGame, ICardGameService> _gameServices = gameServices.ToDictionary(s => s.Game);
    private readonly Lock _cacheLock = new();
    private (DateTimeOffset At, HoldingsValuation Value)? _holdings;
    private (DateTimeOffset At, RealizedSummary Value)? _realized;

    public async Task<DashboardDataDto> GetAsync(DashboardWidgetDto widget, IReadOnlyCollection<int>? siteIds, CancellationToken ct) =>
        widget.Type switch
        {
            "metric" => Metric(widget.Metric),
            "breakdown" => Breakdown(widget.GroupBy),
            "search" => Search(widget, siteIds),
            "sql" => await sql.RunAsync(widget.Database, widget.Sql, ct),
            _ => throw new DashboardException(DashboardErrorKind.BadRequest, "This widget has no data."),
        };

    // --- holdings & sales ---------------------------------------------------------------------------

    private DashboardDataDto Metric(string? metric)
    {
        var key = metric ?? "totalMarket";
        decimal? Holdings(Func<HoldingsValuation, decimal> pick) => pick(GetHoldings());
        decimal? Realized(Func<RealizedSummary, decimal> pick) => pick(GetRealized());

        var (value, kind) = key switch
        {
            "totalUnits" => (Holdings(h => h.TotalUnits), "number"),
            "totalCost" => (Holdings(h => h.TotalCost), "money"),
            "totalMarket" => (Holdings(h => h.TotalMarket), "money"),
            "unrealized" => (Holdings(h => h.TotalMarket - h.TotalCost), "money"),
            "realizedProfit" => (Realized(r => r.TotalProceeds - r.TotalCost - r.TotalFees), "money"),
            "realizedProceeds" => (Realized(r => r.TotalProceeds), "money"),
            "realizedCost" => (Realized(r => r.TotalCost), "money"),
            "realizedFees" => (Realized(r => r.TotalFees), "money"),
            "totalSold" => (Realized(r => r.TotalSold), "number"),
            "shippingCost" => (Realized(r => r.TotalShippingCost), "money"),
            "shippingCharged" => (Realized(r => r.TotalShippingCharged), "money"),
            _ => throw new DashboardException(DashboardErrorKind.BadRequest, $"Unknown total '{metric}'."),
        };
        return new DashboardDataDto { Columns = [new(key, kind)], Rows = [[value]] };
    }

    private DashboardDataDto Breakdown(string? groupBy)
    {
        if (groupBy == "salesGame")
        {
            return new DashboardDataDto
            {
                Columns = [new("group", "text"), new("sold", "number"), new("proceeds", "money"), new("cost", "money"), new("profit", "money")],
                Rows = GetRealized().ByGame
                    .Select(l => new List<object?> { GameLabel(l.Key), l.Count, l.Proceeds, l.Cost, l.Proceeds - l.Cost })
                    .ToList(),
            };
        }

        var holdings = GetHoldings();
        var lines = groupBy switch
        {
            "game" or null => holdings.ByGame.Select(l => l with { Key = GameLabel(l.Key) }),
            "category" => holdings.ByCategory,
            _ => throw new DashboardException(DashboardErrorKind.BadRequest, $"Unknown breakdown '{groupBy}'."),
        };
        return new DashboardDataDto
        {
            Columns = [new("group", "text"), new("units", "number"), new("cost", "money"), new("market", "money")],
            Rows = lines.Select(l => new List<object?> { l.Key, l.Units, l.Cost, l.Market }).ToList(),
        };
    }

    private static string GameLabel(string key) =>
        Enum.TryParse<CardGame>(key, out var game) ? DtoMapping.GameDisplayName(game) : key;

    private HoldingsValuation GetHoldings()
    {
        lock (_cacheLock)
        {
            var now = time.GetUtcNow();
            if (_holdings is { } h && now - h.At < AnalyticsTtl) return h.Value;
            var value = analytics.GetHoldings();
            _holdings = (now, value);
            return value;
        }
    }

    private RealizedSummary GetRealized()
    {
        lock (_cacheLock)
        {
            var now = time.GetUtcNow();
            if (_realized is { } r && now - r.At < AnalyticsTtl) return r.Value;
            var value = analytics.GetRealized();
            _realized = (now, value);
            return value;
        }
    }

    // --- card search --------------------------------------------------------------------------------

    private DashboardDataDto Search(DashboardWidgetDto widget, IReadOnlyCollection<int>? siteIds)
    {
        var game = Enum.TryParse<CardGame>(widget.Game, ignoreCase: true, out var g) && Enum.IsDefined(g) ? g : (CardGame?)null;
        List<CollectionCard> lots;
        try
        {
            using var ctx = dbFactory.CreateDbContext();
            lots = CollectionQueryBuilder.BuildFilteredQuery(ctx, widget.Query ?? "", game, containerFilter: null,
                    filterPreset: null, _gameServices, siteIds)
                .Where(c => !c.IsTraded)
                .ToList();
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            throw new DashboardException(DashboardErrorKind.BadRequest, $"The search couldn't be run: {ex.Message}");
        }
        MarketPriceHydrator.Populate(cardService, lots);

        return widget.Output switch
        {
            "cards" => TopCards(lots, widget.Sort, widget.Limit ?? 10),
            "groups" => Groups(lots, widget.GroupBy ?? "setCode", widget.Sort, widget.Limit ?? 10),
            _ => Summary(lots),
        };
    }

    private static DashboardDataDto Summary(IReadOnlyCollection<CollectionCard> lots) => new()
    {
        Columns = [new("cards", "number"), new("units", "number"), new("cost", "money"), new("market", "money")],
        Rows = [[lots.Count, lots.Sum(c => c.Quantity), lots.Sum(Cost), lots.Sum(Value)]],
    };

    private static DashboardDataDto TopCards(List<CollectionCard> lots, string? sort, int limit)
    {
        var stacks = CollectionController.StackRows(lots);
        var ordered = (sort switch
        {
            "price" => stacks.OrderByDescending(c => c.MarketPrice),
            "quantity" => stacks.OrderByDescending(c => c.Quantity),
            "name" => stacks.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            "added" => stacks.OrderByDescending(c => c.DateAdded),
            _ => stacks.OrderByDescending(Value),
        }).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase);
        return new DashboardDataDto
        {
            Columns =
            [
                new("name", "text"), new("set", "text"), new("number", "text"),
                new("quantity", "number"), new("price", "money"), new("value", "money"),
            ],
            Rows = ordered.Take(limit)
                .Select(c => new List<object?> { c.Name, c.SetCode, c.Number, c.Quantity, c.MarketPrice, Value(c) })
                .ToList(),
            Truncated = stacks.Count > limit,
        };
    }

    private static DashboardDataDto Groups(List<CollectionCard> lots, string groupBy, string? sort, int limit)
    {
        var groups = lots
            .Select(c => (Card: c, Group: CollectionGrouping.KeyOf(groupBy, c)))
            .GroupBy(x => x.Group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(grp => new
            {
                grp.First().Group.Label,
                grp.First().Group.Rank,
                Cards = grp.Count(),
                Units = grp.Sum(x => x.Card.Quantity),
                Market = grp.Sum(x => Value(x.Card)),
                Cost = grp.Sum(x => Cost(x.Card)),
            })
            .ToList();

        var ordered = sort switch
        {
            "units" => groups.OrderByDescending(x => x.Units),
            "cards" => groups.OrderByDescending(x => x.Cards),
            "group" => groups.OrderBy(x => x.Label.Length == 0 ? 1 : 0).ThenBy(x => x.Rank),
            _ => groups.OrderByDescending(x => x.Market),
        };
        return new DashboardDataDto
        {
            Columns = [new("group", "text"), new("cards", "number"), new("units", "number"), new("market", "money"), new("cost", "money")],
            Rows = ordered.ThenBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
                .Take(limit)
                .Select(x => new List<object?> { x.Label.Length == 0 ? null : x.Label, x.Cards, x.Units, x.Market, x.Cost })
                .ToList(),
            Truncated = groups.Count > limit,
        };
    }

    private static decimal Value(CollectionCard c) => c.MarketPrice * c.Quantity;
    private static decimal Cost(CollectionCard c) => (c.PurchasePrice ?? 0m) * c.Quantity;
}
