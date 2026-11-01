using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmniCard.Web.Services;

/// <summary>
/// The OAuth <c>state</c> for an eBay sign-in: base64url JSON <c>{ r, n }</c>, where <c>r</c> is this
/// server's own <c>/api/ebay/callback</c> URL (the eBay token broker redirects the browser there) and
/// <c>n</c> is a single-use nonce. <see cref="Consume"/> rejects callbacks this server didn't start, so
/// nobody can link it to their own eBay account with a forged redirect. Nonces live in memory for
/// <see cref="Lifetime"/>; a restart mid-sign-in just means clicking Connect again.
/// </summary>
public sealed class EbayOAuthStateStore(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _nonces = new();

    public string Create(string returnUrl)
    {
        Prune();
        var nonce = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24));
        _nonces[nonce] = time.GetUtcNow() + Lifetime;
        return Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new StatePayload(returnUrl, nonce)));
    }

    /// <summary>True (once) when <paramref name="state"/> carries a nonce this server issued and it
    /// hasn't expired.</summary>
    public bool Consume(string? state)
    {
        var nonce = Read(state)?.N;
        return nonce is not null
            && _nonces.TryRemove(nonce, out var expires)
            && expires > time.GetUtcNow();
    }

    private static StatePayload? Read(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;
        try
        {
            return JsonSerializer.Deserialize<StatePayload>(Base64Url.DecodeFromChars(state));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private void Prune()
    {
        var now = time.GetUtcNow();
        foreach (var (nonce, expires) in _nonces)
            if (expires <= now) _nonces.TryRemove(nonce, out _);
    }

    // Short lowercase names: the broker reads "r", and they keep eBay's state parameter compact.
    private sealed record StatePayload(
        [property: JsonPropertyName("r")] string R,
        [property: JsonPropertyName("n")] string N);
}
