using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Web.Pages;

public class CardModel : PageModel
{
    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;

    public CardModel(IDbContextFactory<OmniCardDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public CollectionCard Card { get; set; } = null!;

    public IActionResult OnGet(int id)
    {
        using var db = _dbFactory.CreateDbContext();

        var lot = db.Lots
            .AsNoTracking()
            .Include(l => l.Product)
            .FirstOrDefault(l => l.Id == id && l.Product.Category == ProductCategory.Single);

        if (lot is null)
            return NotFound();

        var card = CollectionCardMapper.ToDto(lot, lot.Product, 0m);

        if (lot.LocationId is int locationId)
            card.Container = db.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == locationId);

        Card = card;
        return Page();
    }

    public string? ImageUrl
    {
        get
        {
            if (Card.ScanImagePath is not null)
            {
                // ScanImagePath is "scans/123.jpg" — serve from /scans/123.jpg
                var filename = Path.GetFileName(Card.ScanImagePath);
                return $"/scans/{filename}";
            }
            return Card.ImageUri;
        }
    }
}
