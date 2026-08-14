using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Pages;

public class LocationModel : PageModel
{
    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;
    private readonly ICardService _cardService;
    private readonly IDataPathService _dataPathService;

    public LocationModel(
        IDbContextFactory<OmniCardDbContext> dbFactory,
        ICardService cardService,
        IDataPathService dataPathService)
    {
        _dbFactory = dbFactory;
        _cardService = cardService;
        _dataPathService = dataPathService;
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

        var tagsByLot = TagLookup.GetTagsByLots(db, rawCards.Select(c => c.Id));
        foreach (var c in rawCards)
            c.Tags = tagsByLot.GetValueOrDefault(c.Id, []);

        // Fill in catalog art for cards with no stored ImageUri, same as the desktop collection list.
        CardArtHydrator.HydrateMissingImageUris(_cardService, rawCards);

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
                    CardImageUrl.Resolve(rep.ScanImagePath, rep.ImageUri, _dataPathService.ScansDirectory),
                    rep.MarketPrice > 0m ? rep.MarketPrice : null,
                    rep.Tags);
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
        decimal? MarketPrice,
        List<string> Tags);
}
