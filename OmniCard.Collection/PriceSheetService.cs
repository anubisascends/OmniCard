using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public sealed class PriceSheetService(
    IDbContextFactory<OmniCardDbContext> dbContextFactory,
    ICardService cardService) : IPriceSheetService
{
    public IReadOnlyCollection<CardGame> GetGamesPresent(int containerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.Lots.AsNoTracking()
            .Where(l => l.LocationId == containerId && l.Product.Category == ProductCategory.Single)
            .Select(l => l.Product.Game)
            .Distinct()
            .ToList();
    }

    public bool HasSealedProduct(int containerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.Lots.AsNoTracking()
            .Any(l => l.LocationId == containerId && l.Product.Category != ProductCategory.Single);
    }

    public bool HasAnyProduct(int containerId)
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.Lots.AsNoTracking().Any(l => l.LocationId == containerId);
    }

    public PriceSheetReport BuildReport(int containerId, string containerName)
    {
        using var context = dbContextFactory.CreateDbContext();
        var lots = context.Lots.AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.LocationId == containerId)
            .ToList();

        var lines = new List<(CardGame Game, PriceSheetLine Line)>();

        foreach (var lot in lots)
        {
            var product = lot.Product;
            var isSingle = product.Category == ProductCategory.Single;

            decimal price;
            var name = product.Name;

            if (isSingle)
            {
                price = string.IsNullOrEmpty(product.GameCardId)
                    ? 0m
                    : cardService.GetGameService(product.Game).GetCurrentPrice(product.GameCardId, product.Foil) ?? 0m;

                if (product.Foil)
                    name += " (Foil)";
            }
            else
            {
                price = product.LastMarketPrice ?? 0m;
            }

            var line = new PriceSheetLine
            {
                Name = name,
                SetCode = product.SetCode ?? product.SetName,
                CollectorNumber = isSingle ? product.CollectorNumber : null,
                Price = price,
            };

            var quantity = Math.Max(1, lot.Quantity);
            for (var i = 0; i < quantity; i++)
                lines.Add((product.Game, line));
        }

        var sections = lines
            .GroupBy(x => x.Game)
            .Select(g => new PriceSheetSection
            {
                GameDisplayName = GameDisplayName(g.Key),
                Lines = g.Select(x => x.Line)
                    .OrderBy(l => l.SetCode ?? "", StringComparer.OrdinalIgnoreCase)
                    .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .OrderBy(s => s.GameDisplayName, StringComparer.Ordinal)
            .ToList();

        return new PriceSheetReport
        {
            LocationName = containerName,
            Sections = sections,
        };
    }

    private static string GameDisplayName(CardGame game) => game switch
    {
        CardGame.Mtg => "Magic: The Gathering",
        CardGame.OnePiece => "One Piece TCG",
        CardGame.Riftbound => "Riftbound",
        CardGame.Pokemon => "Pokémon",
        CardGame.YuGiOh => "Yu-Gi-Oh!",
        CardGame.FinalFantasy => "Final Fantasy TCG",
        _ => game.ToString(),
    };
}
