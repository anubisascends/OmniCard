using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IDbContextFactory<CollectionDbContext> _dbFactory;

    public IndexModel(IDbContextFactory<CollectionDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public List<ContainerSummary> Containers { get; set; } = [];

    public void OnGet()
    {
        using var db = _dbFactory.CreateDbContext();
        Containers = db.StorageContainers
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new ContainerSummary
            {
                Id = c.Id,
                Name = c.Name,
                ContainerType = c.ContainerType,
                CardCount = c.Cards.Count,
            })
            .ToList();
    }

    public record ContainerSummary
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public ContainerType ContainerType { get; init; }
        public int CardCount { get; init; }

        public string TypeDisplay => ContainerType switch
        {
            ContainerType.Bulk => "Bulk",
            ContainerType.Binder => "Binder",
            ContainerType.Box => "Box",
            ContainerType.DeckBox => "Deck Box",
            ContainerType.DisplayCase => "Display Case",
            _ => ContainerType.ToString(),
        };
    }
}
