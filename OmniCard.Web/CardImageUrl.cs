namespace OmniCard.Web;

/// <summary>
/// Resolves the browser-facing image URL for a card. A local scan image
/// (<c>ScanImagePath</c>) is served from <c>/scans/&lt;file&gt;</c> and takes
/// precedence over the remote catalog <c>ImageUri</c>. Returns <c>null</c> when
/// neither is available, so callers can render a placeholder.
/// Shared by the Card detail page and the Index search tiles.
/// </summary>
public static class CardImageUrl
{
    public static string? Resolve(string? scanImagePath, string? imageUri)
    {
        if (!string.IsNullOrEmpty(scanImagePath))
            return "/scans/" + Path.GetFileName(scanImagePath);

        return string.IsNullOrEmpty(imageUri) ? null : imageUri;
    }
}
