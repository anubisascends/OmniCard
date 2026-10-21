using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Data.Catalogs;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Inventory;
using OmniCard.Web.Api.Mapping;
using OmniCard.Web.Helpers;

namespace OmniCard.Web.Services.Labels;

/// <summary>
/// Collects the printable facts of owned cards for a label print run — the values a label template's
/// fields are filled from (name, set, number, live market price, TCGplayer link, …). Cards come from a
/// selection (kept in the order given) or from a whole location (in binder order: page, slot, then
/// name), always limited to the sites the user can read.
/// </summary>
public sealed class LabelDataService(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    IEnumerable<ICardGameService> gameServices,
    ICardService cardService,
    IDbContextFactory<ScryfallDbContext>? scryfallFactory = null)
{
    /// <summary>Cards per print run. A run of this size is already hundreds of sheets.</summary>
    public const int MaxCards = 5000;

    private readonly IReadOnlyDictionary<CardGame, ICardGameService> _games = gameServices.ToDictionary(s => s.Game);

    public LabelDataDto Build(LabelDataRequest request, IReadOnlyCollection<int>? siteIds)
    {
        using var ctx = dbFactory.CreateDbContext();

        List<int> ids;
        var truncated = false;
        if (request.LotIds is { Count: > 0 } requested)
        {
            var wanted = requested.Distinct().ToList();
            truncated = wanted.Count > MaxCards;
            wanted = wanted.Take(MaxCards).ToList();
            // Through the filtered query so unreadable sites (and sealed product) drop out.
            var allowed = CollectionQueryBuilder.BuildFilteredQuery(ctx, "", null, null, null, _games, siteIds)
                .Where(c => wanted.Contains(c.Id)).Select(c => c.Id).ToHashSet();
            ids = wanted.Where(allowed.Contains).ToList();
        }
        else if (request.ContainerId is int containerId)
        {
            var game = LocationGame(request.Game);
            var rows = CollectionQueryBuilder.BuildFilteredQuery(ctx, request.Q ?? "", game, containerId, null, _games, siteIds)
                .Select(c => new { c.Id, c.Page, c.Slot, c.Name, c.SetCode, c.Number })
                .ToList()
                .OrderBy(c => c.Page ?? int.MaxValue).ThenBy(c => c.Slot ?? int.MaxValue)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.SetCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Number, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Id)
                .Select(c => c.Id)
                .ToList();
            truncated = rows.Count > MaxCards;
            ids = rows.Take(MaxCards).ToList();
        }
        else
        {
            return new LabelDataDto([], false);
        }

        if (ids.Count == 0) return new LabelDataDto([], truncated);

        var lots = ctx.Lots.AsNoTracking().Include(l => l.Product)
            .Where(l => ids.Contains(l.Id) && l.Product.Category == ProductCategory.Single)
            .ToList();
        var containerIds = lots.Where(l => l.LocationId != null).Select(l => l.LocationId!.Value).Distinct().ToList();
        var containerNames = ctx.StorageContainers.AsNoTracking()
            .Where(c => containerIds.Contains(c.Id))
            .ToDictionary(c => c.Id, c => c.Name);

        var cards = lots.Select(l => (Lot: l, Card: CollectionCardMapper.ToDto(l, l.Product, 0m))).ToList();
        MarketPriceHydrator.Populate(cardService, cards.Select(c => c.Card).ToList());
        var tcg = TcgPlayerLink.ForCards(cards.Select(c => c.Card).ToList(), scryfallFactory);

        var byId = cards.ToDictionary(c => c.Card.Id);
        var result = new List<LabelCardDto>(ids.Count);
        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var entry)) continue;
            var (lot, c) = entry;
            var (url, productId) = tcg[c.Id];

            result.Add(new LabelCardDto
            {
                Id = c.Id,
                Game = DtoMapping.GameId(c.Game),
                GameName = DtoMapping.GameDisplayName(c.Game),
                Name = c.Name,
                SetCode = c.SetCode,
                SetName = c.SetName,
                CollectorNumber = c.Number,
                Rarity = c.Rarity,
                Condition = c.Condition,
                Language = c.Language,
                Finish = !string.IsNullOrWhiteSpace(c.FoilType) ? c.FoilType! : c.IsFoil ? "Foil" : "",
                Quantity = Math.Max(1, lot.Quantity),
                MarketPrice = c.MarketPrice,
                TcgPlayerUrl = url,
                TcgPlayerId = productId,
                Location = lot.LocationId is int loc ? containerNames.GetValueOrDefault(loc) : null,
            });
        }
        return new LabelDataDto(result, truncated);
    }

    private static CardGame? LocationGame(string? game) =>
        Enum.TryParse<CardGame>(game, ignoreCase: true, out var g) ? g : null;
}
