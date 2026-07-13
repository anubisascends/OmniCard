using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Web.Pages;

public class LocationModel : PageModel
{
    private readonly IDbContextFactory<CollectionDbContext> _dbFactory;

    public LocationModel(IDbContextFactory<CollectionDbContext> dbFactory)
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

        var rawCards = db.Cards
            .AsNoTracking()
            .Where(c => c.ContainerId == id)
            .OrderBy(c => c.Name)
            .ToList();

        CardCount = rawCards.Count;

        Cards = rawCards
            .GroupBy(c => new { c.Name, c.SetCode })
            .Select(g =>
            {
                var first = g.First();
                return new StackedCard(
                    first.Id,
                    first.Name,
                    first.SetCode,
                    first.Number,
                    first.Rarity,
                    first.Color,
                    g.Count());
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
        string SetCode,
        string Number,
        string Rarity,
        string? Color,
        int Quantity);
}
