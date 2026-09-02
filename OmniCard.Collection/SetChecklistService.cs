using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

/// <summary>Combines a game's full set catalog (<see cref="ICardGameService.GetSetCards"/>) with
/// the user's owned singles to produce an ownership checklist and a printable want-list. Ownership
/// is matched to catalog printings the same way the location audit does: by GameCardId first, then
/// falling back to normalized (set code + collector number).</summary>
public class SetChecklistService : ISetChecklistService
{
    private readonly IReadOnlyDictionary<CardGame, ICardGameService> _gameServices;
    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;

    public SetChecklistService(
        IEnumerable<ICardGameService> gameServices,
        IDbContextFactory<OmniCardDbContext> dbFactory)
    {
        _gameServices = gameServices.ToDictionary(s => s.Game);
        _dbFactory = dbFactory;
    }

    public Task<SetChecklist> BuildAsync(CardGame game, string setCode)
    {
        if (!_gameServices.TryGetValue(game, out var svc))
            throw new ArgumentException($"No card-game service registered for {game}", nameof(game));

        var catalog = svc.GetSetCards(setCode); // already sorted by collector number

        // Owned singles for this game+set. Traded-away lots are not physically owned, so exclude them.
        // Set code is compared case-insensitively so imported cards whose casing differs from the
        // catalog (e.g. "set1" vs "SET1") still count toward ownership.
        var lowered = setCode.ToLowerInvariant();
        using var db = _dbFactory.CreateDbContext();
        var ownedLots = db.Lots.AsNoTracking()
            .Where(l => l.Product.Category == ProductCategory.Single
                        && l.Product.Game == game
                        && l.Product.SetCode != null
                        && l.Product.SetCode.ToLower() == lowered
                        && !l.IsTraded)
            .Select(l => new { l.Product.GameCardId, l.Product.SetCode, l.Product.CollectorNumber, l.Quantity })
            .ToList();

        // Index the catalog for the two-stage match (GameCardId, then set+collector).
        var byId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byNumber = new Dictionary<(string, string), int>();
        for (var i = 0; i < catalog.Count; i++)
        {
            var c = catalog[i];
            if (!string.IsNullOrWhiteSpace(c.GameCardId))
                byId[c.GameCardId] = i;
            byNumber[(Norm(c.SetCode), Norm(c.CollectorNumber))] = i;
        }

        var quantities = new int[catalog.Count];
        foreach (var lot in ownedLots)
        {
            var idx = -1;
            if (!string.IsNullOrWhiteSpace(lot.GameCardId) && byId.TryGetValue(lot.GameCardId, out var byIdx))
                idx = byIdx;
            else if (byNumber.TryGetValue((Norm(lot.SetCode), Norm(lot.CollectorNumber)), out var byNumIdx))
                idx = byNumIdx;

            if (idx >= 0)
                quantities[idx] += Math.Max(0, lot.Quantity);
        }

        var cards = new List<SetChecklistCard>(catalog.Count);
        for (var i = 0; i < catalog.Count; i++)
        {
            var c = catalog[i];
            var qty = quantities[i];
            cards.Add(new SetChecklistCard
            {
                GameCardId = c.GameCardId,
                CollectorNumber = c.CollectorNumber,
                Name = c.Name,
                Rarity = c.Rarity,
                OwnedQuantity = qty,
                NormalPrice = c.NormalPrice,
                FoilPrice = c.FoilPrice,
                HasFoil = c.HasFoil,
                Card = new CollectionCard
                {
                    Game = game,
                    GameCardId = c.GameCardId,
                    Name = c.Name,
                    SetCode = c.SetCode,
                    SetName = c.SetName,
                    Number = c.CollectorNumber,
                    Rarity = c.Rarity,
                    ImageUri = c.ImageUri,
                    MarketPrice = c.NormalPrice ?? 0m,
                    Quantity = qty,
                },
            });
        }

        var ownedDistinct = quantities.Count(q => q > 0);

        // Guarantee collector-number order even if a game service's GetSetCards didn't sort.
        var ordered = cards
            .OrderBy(c => c.CollectorNumber, CollectorNumberComparer.Instance)
            .ToList();

        return Task.FromResult(new SetChecklist
        {
            Game = game,
            SetCode = setCode,
            SetName = catalog.Count > 0 ? catalog[0].SetName : setCode,
            Cards = ordered,
            OwnedCount = ownedDistinct,
            TotalCount = catalog.Count,
            OwnedPhysicalCount = quantities.Sum(),
        });
    }

    public SetChecklistReport BuildWantListReport(SetChecklist checklist)
    {
        var rows = checklist.Cards
            .Where(c => !c.Owned)
            .Select(c => new WantListRow(c.CollectorNumber, c.Name, c.Rarity, c.NormalPrice, c.FoilPrice))
            .ToList();

        return new SetChecklistReport
        {
            Game = checklist.Game,
            SetCode = checklist.SetCode,
            SetName = checklist.SetName,
            OwnedCount = checklist.OwnedCount,
            TotalCount = checklist.TotalCount,
            Rows = rows,
            AnyFoil = rows.Any(r => r.FoilPrice.HasValue),
        };
    }

    private static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();
}
