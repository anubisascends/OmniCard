using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Pages;

/// <summary>Records a trade for a physical card from the phone: a note + photo, written to a
/// shared folder for the desktop app to pick up on next launch. Never writes to the (read-only)
/// collection database — see OmniCard.Web's read-only invariant.</summary>
public class TradeModel : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;
    private readonly IDataPathService _dataPathService;

    public TradeModel(IDbContextFactory<OmniCardDbContext> dbFactory, IDataPathService dataPathService)
    {
        _dbFactory = dbFactory;
        _dataPathService = dataPathService;
    }

    public CollectionCard Card { get; set; } = null!;

    public string? ImageUrl => CardImageUrl.Resolve(Card.ScanImagePath, Card.ImageUri);

    public IActionResult OnGet(int lotId)
    {
        using var db = _dbFactory.CreateDbContext();
        var lot = db.Lots.AsNoTracking().Include(l => l.Product)
            .FirstOrDefault(l => l.Id == lotId && l.Product.Category == ProductCategory.Single);

        if (lot is null)
            return NotFound();
        if (lot.IsTraded)
            return RedirectToPage("Card", new { id = lotId });

        Card = CollectionCardMapper.ToDto(lot, lot.Product, 0m);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int lotId, string note, IFormFile? photo)
    {
        using (var db = _dbFactory.CreateDbContext())
        {
            var exists = db.Lots.AsNoTracking()
                .Any(l => l.Id == lotId && l.Product.Category == ProductCategory.Single);
            if (!exists)
                return NotFound();
        }

        var tradeId = Guid.NewGuid();
        var folder = Path.Combine(_dataPathService.TradesDirectory, tradeId.ToString());
        Directory.CreateDirectory(folder);

        var photoFileName = "";
        if (photo is { Length: > 0 })
        {
            var ext = Path.GetExtension(photo.FileName);
            photoFileName = "photo" + (string.IsNullOrEmpty(ext) ? ".jpg" : ext);
            await using var stream = System.IO.File.Create(Path.Combine(folder, photoFileName));
            await photo.CopyToAsync(stream);
        }

        var record = new TradeRecord
        {
            TradeId = tradeId,
            LotId = lotId,
            Note = note ?? "",
            PhotoFileName = photoFileName,
        };
        await System.IO.File.WriteAllTextAsync(
            Path.Combine(folder, "trade.json"),
            JsonSerializer.Serialize(record, JsonOptions));

        TempData["TradeMessage"] = "Trade recorded — it'll be applied next time the desktop app opens.";
        return RedirectToPage("Card", new { id = lotId });
    }
}
