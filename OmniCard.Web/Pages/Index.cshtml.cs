using Microsoft.AspNetCore.Mvc;
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

    [BindProperty(SupportsGet = true)]
    public string? Game { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public List<ContainerSummary> Containers { get; set; } = [];
    public List<CardSearchResult> SearchResults { get; set; } = [];
    public bool IsSearchActive => !string.IsNullOrWhiteSpace(Q);

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
            _ => null
        };
    }

    private void LoadContainers()
    {
        var gameFilter = ParseGameFilter();
        using var db = _dbFactory.CreateDbContext();

        var query = db.StorageContainers
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new ContainerSummary
            {
                Id = c.Id,
                Name = c.Name,
                ContainerType = c.ContainerType,
                CardCount = gameFilter.HasValue
                    ? c.Cards.Count(card => card.Game == gameFilter.Value)
                    : c.Cards.Count,
            });

        var results = query.ToList();

        Containers = gameFilter.HasValue
            ? results.Where(c => c.CardCount > 0).ToList()
            : results;
    }

    private void ExecuteSearch()
    {
        var gameFilter = ParseGameFilter();
        using var db = _dbFactory.CreateDbContext();

        IQueryable<CollectionCard> query = db.Cards.AsNoTracking();

        if (gameFilter.HasValue)
            query = query.Where(c => c.Game == gameFilter.Value);

        var terms = Q!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var term in terms)
        {
            if (term.StartsWith("set:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[4..];
                var pattern = $"%{val}%";
                query = query.Where(c =>
                    EF.Functions.Like(c.SetCode, pattern) ||
                    EF.Functions.Like(c.SetName, pattern));
            }
            else if (term.StartsWith("cn:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[3..];
                var pattern = $"%{val}%";
                query = query.Where(c => EF.Functions.Like(c.Number, pattern));
            }
            else if (term.StartsWith("rarity:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[7..];
                var pattern = $"%{val}%";
                query = query.Where(c => EF.Functions.Like(c.Rarity, pattern));
            }
            else if (term.StartsWith("r:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[2..];
                var pattern = $"%{val}%";
                query = query.Where(c => EF.Functions.Like(c.Rarity, pattern));
            }
            else if (term.StartsWith("color:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[6..];
                var pattern = $"%{val}%";
                query = query.Where(c => c.Color != null && EF.Functions.Like(c.Color, pattern));
            }
            else if (term.StartsWith("c:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[2..];
                var pattern = $"%{val}%";
                query = query.Where(c => c.Color != null && EF.Functions.Like(c.Color, pattern));
            }
            else if (term.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[5..];
                var pattern = $"%{val}%";
                query = query.Where(c => c.CardType != null && EF.Functions.Like(c.CardType, pattern));
            }
            else if (term.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[2..];
                var pattern = $"%{val}%";
                query = query.Where(c => c.CardType != null && EF.Functions.Like(c.CardType, pattern));
            }
            else
            {
                var pattern = $"%{term}%";
                query = query.Where(c => EF.Functions.Like(c.Name, pattern));
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
