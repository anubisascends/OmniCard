using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Web.Pages;

public class LocationModel : PageModel
{
    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;

    public LocationModel(IDbContextFactory<OmniCardDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public StorageContainer Container { get; set; } = null!;
    public int CardCount { get; set; }
    public List<SetSummary> Sets { get; set; } = [];
    public List<StackedCard> Cards { get; set; } = [];

    public IActionResult OnGet(int id)
    {
        using var db = _dbFactory.CreateDbContext();

        var container = db.StorageContainers
            .AsNoTracking()
            .FirstOrDefault(c => c.Id == id);

        if (container is null)
            return NotFound();

        Container = container;

        var rawCards = db.Lots
            .AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.LocationId == id && l.Product.Category == ProductCategory.Single)
            .ToList()
            .Select(l => CollectionCardMapper.ToDto(l, l.Product, l.Product.LastMarketPrice ?? 0m))
            .OrderBy(c => c.Name)
            .ToList();

        CardCount = rawCards.Count;

        Cards = rawCards
            .GroupBy(c => new { c.Name, c.SetCode })
            .Select(g =>
            {
                // Representative copy (lowest Id) supplies set name, art, and price.
                var rep = g.OrderBy(c => c.Id).First();
                return new StackedCard(
                    rep.Id,
                    rep.Name,
                    rep.SetName,
                    rep.SetCode,
                    rep.Number,
                    rep.Rarity,
                    rep.Color,
                    g.Count(),
                    CardImageUrl.Resolve(rep.ScanImagePath, rep.ImageUri),
                    rep.MarketPrice > 0m ? rep.MarketPrice : null);
            })
            .OrderBy(c => c.Name)
            .ToList();

        Sets = rawCards
            .GroupBy(c => new { c.SetCode, c.SetName })
            .Select(g => new SetSummary
            {
                SetCode = g.Key.SetCode,
                SetName = g.Key.SetName,
                Count = g.Count(),
            })
            .OrderBy(s => s.SetName)
            .ToList();

        return Page();
    }

    public string TypeDisplay => Container.ContainerType switch
    {
        ContainerType.Bulk => "Bulk",
        ContainerType.Binder => "Binder",
        ContainerType.Box => "Box",
        ContainerType.DeckBox => "Deck Box",
        ContainerType.DisplayCase => "Display Case",
        _ => Container.ContainerType.ToString(),
    };

    public record SetSummary
    {
        public string SetCode { get; init; } = "";
        public string SetName { get; init; } = "";
        public int Count { get; init; }
    }

    public record StackedCard(
        int Id,
        string Name,
        string SetName,
        string SetCode,
        string Number,
        string Rarity,
        string? Color,
        int Quantity,
        string? ImageUrl,
        decimal? MarketPrice);
}
