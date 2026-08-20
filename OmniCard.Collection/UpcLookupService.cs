using System.Net.Http;
using System.Text.Json;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

/// <summary>
/// Resolves product details from a UPC via the UPCitemdb trial API
/// (https://www.upcitemdb.com/api/explorer). The trial endpoint needs no API key but is
/// rate-limited (~100 lookups/day). All failures are swallowed into a <c>null</c> result so
/// the caller can silently fall back to manual entry.
/// </summary>
public class UpcLookupService(IHttpClientFactory httpClientFactory) : IUpcLookupService
{
    private const string TrialEndpoint = "https://api.upcitemdb.com/prod/trial/lookup?upc=";

    public async Task<UpcLookupResult?> LookupAsync(string upc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(upc)) return null;

        try
        {
            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            using var response = await client.GetAsync(TrialEndpoint + Uri.EscapeDataString(upc.Trim()), cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var root = doc.RootElement;
            if (!root.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array
                || items.GetArrayLength() == 0)
            {
                return null;
            }

            var item = items[0];
            return new UpcLookupResult(
                Title: GetString(item, "title"),
                Brand: GetString(item, "brand"),
                Description: GetString(item, "description"),
                Category: GetString(item, "category"),
                ImageUrl: GetFirstImage(item));
        }
        catch
        {
            // Best-effort: any network/parse failure just means "no info found".
            return null;
        }
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? NullIfEmpty(value.GetString())
            : null;

    private static string? GetFirstImage(JsonElement item)
    {
        if (!item.TryGetProperty("images", out var images)
            || images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var image in images.EnumerateArray())
        {
            if (image.ValueKind == JsonValueKind.String)
            {
                var url = NullIfEmpty(image.GetString());
                if (url is not null) return url;
            }
        }

        return null;
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
