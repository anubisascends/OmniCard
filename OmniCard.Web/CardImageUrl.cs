namespace OmniCard.Web;

/// <summary>
/// Resolves the browser-facing image URL for a card, matching the desktop art pipeline
/// (<see cref="OmniCard.Models.CardArtCandidateResolver"/>): the downloaded/catalog art
/// (<c>ImageUri</c>) is preferred, and the local scan (served from <c>/scans/&lt;file&gt;</c>)
/// is only a fallback. Returns <c>null</c> when no art is available, so callers can render a
/// placeholder. Shared by the Card detail page, the Location/Binder views, and the Index search tiles.
/// </summary>
public static class CardImageUrl
{
    public static string? Resolve(string? scanImagePath, string? imageUri, string? scansDirectory = null)
    {
        // Catalog/downloaded art first — same order as the desktop's CardArtCandidateResolver.
        // A stored scan path can point at a file that no longer exists (scans get archived or
        // cleaned up while the DB row keeps the path), so preferring the scan would 404 and hide
        // perfectly good catalog art.
        if (!string.IsNullOrEmpty(imageUri))
            return imageUri;

        if (!string.IsNullOrEmpty(scanImagePath))
        {
            var fileName = Path.GetFileName(scanImagePath);
            // When the scans folder is known, only serve a scan that actually exists on disk —
            // otherwise the tile would 404. Mirrors the desktop, which shows a placeholder for a
            // missing scan file rather than a broken image.
            if (scansDirectory is null || File.Exists(Path.Combine(scansDirectory, fileName)))
                return "/scans/" + fileName;
        }

        return null;
    }
}
