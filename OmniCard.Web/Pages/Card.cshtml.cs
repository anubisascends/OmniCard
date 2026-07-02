using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;

namespace OmniCard.Web.Pages;

public class CardModel : PageModel
{
    private readonly IDbContextFactory<CollectionDbContext> _dbFactory;

    public CardModel(IDbContextFactory<CollectionDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public OmniCard.Models.CollectionCard Card { get; set; } = null!;

    public IActionResult OnGet(int id)
    {
        using var db = _dbFactory.CreateDbContext();

        var card = db.Cards
            .AsNoTracking()
            .Include(c => c.Container)
            .FirstOrDefault(c => c.Id == id);

        if (card is null)
            return NotFound();

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
