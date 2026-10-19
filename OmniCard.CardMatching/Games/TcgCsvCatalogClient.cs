using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OmniCard.Shared.Games;

namespace OmniCard.CardMatching.Games;

/// <summary>
/// Fetches one TCGCSV category (a game) — its groups (sets), every group's products and every group's
/// price rows — and holds the small parsing helpers shared by the services whose catalog is built from
/// TCGCSV products (Riftbound, One Piece). A failure fetching one group's products/prices is logged and
/// skipped; failing to fetch the group list itself throws, since then there is nothing to import.
/// </summary>
internal static partial class TcgCsvCatalogClient
{
    public const string BaseUrl = "https://tcgcsv.com";

    // TCGCSV returns camelCase JSON.
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static async Task<List<TcgCsvGroup>> FetchGroupsAsync(HttpClient client, int categoryId, CancellationToken ct)
    {
        var groups = await client.GetFromJsonAsync<TcgCsvGroupsResponse>(
            $"{BaseUrl}/tcgplayer/{categoryId}/groups", JsonOptions, ct)
            ?? throw new InvalidOperationException($"TCGCSV returned no set list for category {categoryId}.");
        return groups.Results;
    }

    /// <summary>Every group with its products. <paramref name="onGroupDone"/>(done, total) after each group.</summary>
    public static async Task<List<(TcgCsvGroup Group, List<TcgCsvProduct> Products)>> FetchProductsAsync(
        HttpClient client, int categoryId, IReadOnlyList<TcgCsvGroup> groups, ILogger logger,
        Action<int, int>? onGroupDone, CancellationToken ct)
    {
        var result = new List<(TcgCsvGroup, List<TcgCsvProduct>)>();
        var resultLock = new object();
        var done = 0;
        await Parallel.ForEachAsync(groups, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (group, token) =>
            {
                try
                {
                    var products = await client.GetFromJsonAsync<TcgCsvProductsResponse>(
                        $"{BaseUrl}/tcgplayer/{categoryId}/{group.GroupId}/products", JsonOptions, token);
                    lock (resultLock) result.Add((group, products?.Results ?? []));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Failed to fetch TCGCSV products for category {Category} group {GroupId}; skipping",
                        categoryId, group.GroupId);
                }
                finally
                {
                    onGroupDone?.Invoke(Interlocked.Increment(ref done), groups.Count);
                }
            });
        // Parallel completion order is arbitrary; keep the import deterministic.
        return result.OrderBy(r => r.Item1.GroupId).ToList();
    }

    /// <summary>Every group's price rows, grouped by product id (a product has one row per subtype).</summary>
    public static async Task<Dictionary<int, List<TcgCsvPrice>>> FetchPriceRowsAsync(
        HttpClient client, int categoryId, IReadOnlyList<TcgCsvGroup> groups, ILogger logger,
        Action<int, int>? onGroupDone, CancellationToken ct)
    {
        var byProduct = new Dictionary<int, List<TcgCsvPrice>>();
        var done = 0;
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var prices = await client.GetFromJsonAsync<TcgCsvPricesResponse>(
                    $"{BaseUrl}/tcgplayer/{categoryId}/{group.GroupId}/prices", JsonOptions, ct);
                foreach (var row in prices?.Results ?? [])
                {
                    if (!byProduct.TryGetValue(row.ProductId, out var list))
                        byProduct[row.ProductId] = list = [];
                    list.Add(row);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to fetch TCGCSV prices for category {Category} group {GroupId}; skipping",
                    categoryId, group.GroupId);
            }
            onGroupDone?.Invoke(++done, groups.Count);
        }
        return byProduct;
    }

    /// <summary>The value of a product's extended-data field (case-insensitive name), trimmed, or null.</summary>
    public static string? Extended(TcgCsvProduct p, string name)
    {
        var value = p.ExtendedData
            .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>The card name without TCGplayer's trailing variant qualifiers:
    /// "Shanks (020) (Alternate Art)" → "Shanks", "Vex (Overnumbered)" → "Vex".</summary>
    public static string CardName(string productName)
    {
        var name = TrailingParenthetical().Replace(productName, "").Trim();
        return name.Length > 0 ? name : productName.Trim();
    }

    /// <summary>The trailing parenthesized qualifiers of a product name, outermost last:
    /// "Shanks (020) (Alternate Art)" → ["020", "Alternate Art"].</summary>
    public static List<string> Qualifiers(string productName)
    {
        var qualifiers = new List<string>();
        var rest = productName.Trim();
        while (TrailingQualifier().Match(rest) is { Success: true } m)
        {
            qualifiers.Insert(0, m.Groups[1].Value.Trim());
            rest = rest[..m.Index].TrimEnd();
        }
        return qualifiers;
    }

    public static int? LeadingInt(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var m = Digits().Match(s);
        return m.Success && int.TryParse(m.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>TCGCSV rules text is light HTML (&lt;br&gt;, &lt;em&gt;, &lt;strong&gt;): line breaks kept, tags dropped.</summary>
    public static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var text = LineBreak().Replace(html, "\n");
        text = Tag().Replace(text, "");
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }

    /// <summary>A multi-value field ("Red;Green") joined the way the catalogs store it ("Red/Green").</summary>
    public static string? JoinList(string? value)
    {
        if (value is null) return null;
        var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : string.Join("/", parts);
    }

    /// <summary>The product's picture at 400px (TCGCSV links 200px; 400px is enough for usable perceptual
    /// hashing), or null when TCGplayer has none yet (imageCount 0 — the URL would 403).</summary>
    public static string? ProductImage(TcgCsvProduct p) =>
        p.ImageCount == 0 ? null : p.ImageUrl?.Replace("_200w.", "_400w.");

    [GeneratedRegex(@"(\s*\([^()]*\))+\s*$")]
    private static partial Regex TrailingParenthetical();

    [GeneratedRegex(@"\(([^()]*)\)\s*$")]
    private static partial Regex TrailingQualifier();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    [GeneratedRegex(@"\r?\n?<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();
}
