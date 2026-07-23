using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;

    public IndexModel(IDbContextFactory<OmniCardDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    [BindProperty(SupportsGet = true)]
    public string? Game { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public List<ContainerSummary> Containers { get; set; } = [];
    public List<CardSearchResult> SearchResults { get; set; } = [];
    public bool IsSearchActive => !string.IsNullOrWhiteSpace(Q);

    public IEnumerable<IGrouping<ContainerType, ContainerSummary>> ContainersByType =>
        Containers.GroupBy(c => c.ContainerType)
            .OrderBy(g => g.Key);

    public void OnGet()
    {
        if (IsSearchActive)
            ExecuteSearch();
        else
            LoadContainers();
    }

    private CardGame? ParseGameFilter()
    {
        return Game?.ToLowerInvariant() switch
        {
            "mtg" => CardGame.Mtg,
            "optcg" => CardGame.OnePiece,
            "riftbound" => CardGame.Riftbound,
            "pokemon" => CardGame.Pokemon,
            "yugioh" => CardGame.YuGiOh,
            "fftcg" => CardGame.FinalFantasy,
            _ => null
        };
    }

    private void LoadContainers()
    {
        var gameFilter = ParseGameFilter();
        using var db = _dbFactory.CreateDbContext();

        // Lightweight (LocationId, Game) projection — cheaper than materializing full DTOs
        // just to count cards per container.
        var lots = db.Lots.AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.Product.Category == ProductCategory.Single)
            .Select(l => new { l.LocationId, l.Product.Game })
            .ToList();

        if (gameFilter.HasValue)
            lots = lots.Where(l => l.Game == gameFilter.Value).ToList();

        var countsByContainer = lots
            .Where(l => l.LocationId.HasValue)
            .GroupBy(l => l.LocationId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var containers = db.StorageContainers
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToList()
            .Select(c => new ContainerSummary
            {
                Id = c.Id,
                Name = c.Name,
                ContainerType = c.ContainerType,
                CardCount = countsByContainer.GetValueOrDefault(c.Id),
            })
            .ToList();

        Containers = gameFilter.HasValue
            ? containers.Where(c => c.CardCount > 0).ToList()
            : containers;
    }

    private void ExecuteSearch()
    {
        var gameFilter = ParseGameFilter();
        using var db = _dbFactory.CreateDbContext();

        // Project Lots⋈Products (owned singles) into the CollectionCard DTO shape via the shared
        // mapper, same as the desktop app's read facade (CardService), then filter in memory —
        // this companion view serves one user's own collection, not a multi-tenant dataset.
        IEnumerable<CollectionCard> query = db.Lots.AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.Product.Category == ProductCategory.Single)
            .ToList()
            .Select(l => CollectionCardMapper.ToDto(l, l.Product, 0m));

        if (gameFilter.HasValue)
            query = query.Where(c => c.Game == gameFilter.Value);

        var terms = Q!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var term in terms)
        {
            if (term.StartsWith("set:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[4..];
                query = query.Where(c => Contains(c.SetCode, val) || Contains(c.SetName, val));
            }
            else if (term.StartsWith("cn:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[3..];
                query = query.Where(c => Contains(c.Number, val));
            }
            else if (term.StartsWith("rarity:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[7..];
                query = query.Where(c => Contains(c.Rarity, val));
            }
            else if (term.StartsWith("r:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[2..];
                query = query.Where(c => Contains(c.Rarity, val));
            }
            else if (term.StartsWith("color:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[6..];
                query = query.Where(c => Contains(c.Color, val));
            }
            else if (term.StartsWith("c:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[2..];
                query = query.Where(c => Contains(c.Color, val));
            }
            else if (term.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[5..];
                query = query.Where(c => Contains(c.CardType, val));
            }
            else if (term.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[2..];
                query = query.Where(c => Contains(c.CardType, val));
            }
            else
            {
                query = query.Where(c => Contains(c.Name, term));
            }
        }

        SearchResults = query
            .GroupBy(c => new { c.Name, c.SetCode })
            .Select(g => new CardSearchResult
            {
                Id = g.Min(c => c.Id),
                Name = g.Key.Name,
                SetCode = g.Key.SetCode,
                Number = g.Min(c => c.Number),
                Rarity = g.Min(c => c.Rarity),
                Color = g.Min(c => c.Color),
                Quantity = g.Count(),
            })
            .OrderBy(r => r.Name)
            .ThenBy(r => r.SetCode)
            .ToList();
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

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

    public record CardSearchResult
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string SetCode { get; init; } = "";
        public string Number { get; init; } = "";
        public string Rarity { get; init; } = "";
        public string? Color { get; init; }
        public int Quantity { get; init; }
    }
}
