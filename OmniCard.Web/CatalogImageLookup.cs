using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web;

/// <summary>
/// Resolves the browser-facing image URL for a card, in three tiers:
/// 1. A local scan file — but only if it's actually still on disk. <c>InventoryLot.ScanImagePath</c>
///    can point at a file that no longer exists (e.g. a scan committed under the Store workflow,
///    later archived-and-deleted after switching to Discard) — trusting the DB value blindly would
///    render a broken image instead of falling through to the remaining tiers.
/// 2. The catalog <c>ImageUri</c> captured on the <c>Product</c> at commit time.
/// 3. A live lookup against the card's game catalog DB by <c>GameCardId</c>, for cards whose
///    <c>ImageUri</c> was never captured (older rows, CSV/decklist imports).
/// Returns null only when none of the three tiers can produce an image.
/// </summary>
public class CatalogImageLookup(IEnumerable<ICardGameService> gameServices, IDataPathService dataPathService)
{
    public string? Resolve(CardGame game, string? gameCardId, string? scanImagePath, string? imageUri)
    {
        if (!string.IsNullOrEmpty(scanImagePath))
        {
            var fileName = Path.GetFileName(scanImagePath);
            if (File.Exists(Path.Combine(dataPathService.ScansDirectory, fileName)))
                return "/scans/" + fileName;
        }

        if (!string.IsNullOrEmpty(imageUri))
            return imageUri;

        if (string.IsNullOrEmpty(gameCardId))
            return null;

        var service = gameServices.FirstOrDefault(s => s.Game == game);
        return CardImageUriResolver.From(service?.FindCardById(gameCardId));
    }
}
