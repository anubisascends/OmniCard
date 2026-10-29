namespace OmniCard.eBay;

/// <summary>
/// Pulls the OAuth authorization code out of whatever the user pasted after approving access on eBay:
/// the full address eBay sent their browser to (any accepted URL — eBay's own "thank you" page, or even
/// a page that failed to load, since the code is in the query string), or the bare code itself.
/// This is what lets a self-hosted server connect without a reachable callback URL.
/// </summary>
public static class EbayAuthCodeParser
{
    /// <summary>Returns the decoded authorization code, or null when the input has none — including when
    /// eBay reported that access was declined (<c>isAuthSuccessful=false</c> / <c>error=…</c>).</summary>
    public static string? ExtractCode(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;

        var isUrl = text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var queryStart = text.IndexOf('?');

        if (isUrl || text.Contains("code=", StringComparison.Ordinal))
        {
            // Without a '?', a non-URL is just the query ("code=…&expires_in=…") pasted on its own.
            var query = queryStart >= 0 ? text[(queryStart + 1)..] : isUrl ? null : text;
            if (query is null)
                return null;

            string? code = null;
            // Raw codes contain '#', so don't treat it as a fragment marker — eBay percent-encodes it.
            foreach (var pair in query.Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                var key = pair[..eq];
                var value = Unescape(pair[(eq + 1)..]);

                if (key.Equals("error", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                    return null;
                if (key.Equals("isAuthSuccessful", StringComparison.OrdinalIgnoreCase)
                    && value.Equals("false", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (key.Equals("code", StringComparison.OrdinalIgnoreCase))
                    code = value;
            }
            return string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        }

        // A bare code: eBay's look like "v^1.1#i^1#…"; decode it if it was copied still percent-encoded.
        return text.Contains(' ') ? null : Unescape(text);
    }

    private static string Unescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }
}
