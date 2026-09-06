using System.Security.Cryptography;
using System.Text;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Services;

/// <summary>
/// Stores card artwork on the server filesystem (under <c>{dataDir}/card-images</c>) so the web app
/// self-hosts images instead of hot-linking external CDNs at view time. Files are keyed
/// deterministically by (game, card id) with the source URL's extension; the DB is not touched (the
/// card's stored/derived CDN URL remains the fallback). Served over HTTP at <c>/card-images</c>.
/// </summary>
public sealed class CardImageCacheService
{
    private readonly string _root;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CardImageCacheService> _logger;

    public const string RequestPath = "/card-images";

    public CardImageCacheService(IDataPathService dataPath, IHttpClientFactory httpClientFactory,
        ILogger<CardImageCacheService> logger)
    {
        _root = Path.Combine(dataPath.DataDirectory, "card-images");
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Root directory served at <see cref="RequestPath"/>.</summary>
    public string Root => _root;

    /// <summary>Relative cache path for a card, e.g. <c>Mtg/1a2b….jpg</c>. Deterministic: a SHA-1 of
    /// the card id (filesystem-safe, avoids odd characters) plus the source URL's extension.</summary>
    public string RelativePath(CardGame game, string cardId, string? sourceUrl)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(cardId))).ToLowerInvariant();
        var ext = ExtensionOf(sourceUrl);
        return $"{game}/{hash}{ext}";
    }

    /// <summary>The public URL for a cached card image, or null if not cached yet.</summary>
    public string? DisplayUrl(CardGame game, string cardId, string? sourceUrl)
    {
        if (string.IsNullOrEmpty(cardId))
            return null;
        var rel = RelativePath(game, cardId, sourceUrl);
        return File.Exists(Path.Combine(_root, rel)) ? $"{RequestPath}/{rel}" : null;
    }

    /// <summary>Downloads the image to the cache if it isn't already present. Returns true when the
    /// file exists afterward (already-cached counts as success). Never throws — logs and returns false.</summary>
    public async Task<bool> EnsureCachedAsync(CardGame game, string cardId, string? sourceUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(cardId) || string.IsNullOrWhiteSpace(sourceUrl))
            return false;

        var rel = RelativePath(game, cardId, sourceUrl);
        var full = Path.Combine(_root, rel);
        if (File.Exists(full))
            return true;

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var resp = await client.GetAsync(sourceUrl, ct);
            if (!resp.IsSuccessStatusCode)
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            // Write via a temp file + move so a concurrent reader never sees a half-written image.
            var tmp = full + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            File.Move(tmp, full, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to cache image for {Game} {CardId} from {Url}", game, cardId, sourceUrl);
            return false;
        }
    }

    /// <summary>Rewrites each card's <see cref="CollectionCard.ImageUri"/> to the locally-cached URL
    /// when the image has been downloaded, so the SPA serves art from this server instead of the CDN.
    /// Cards not yet cached keep their existing (CDN) URL. Call after art hydration, before mapping.</summary>
    public void PreferCached(IReadOnlyList<CollectionCard> cards)
    {
        foreach (var card in cards)
        {
            if (string.IsNullOrEmpty(card.GameCardId) || string.IsNullOrEmpty(card.ImageUri))
                continue;
            var local = DisplayUrl(card.Game, card.GameCardId, card.ImageUri);
            if (local is not null)
                card.ImageUri = local;
        }
    }

    private static string ExtensionOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return ".jpg";
        // Strip query string, take the last path extension; only trust common image types.
        var path = url.Split('?', '#')[0];
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" ? ext : ".jpg";
    }
}
