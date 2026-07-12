using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public sealed partial class DecklistService(
    IDbContextFactory<CollectionDbContext> dbContextFactory,
    IHttpClientFactory httpClientFactory,
    ICardService cardService) : IDecklistService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // Regex: "1 Card Name" or "1 Card Name (SET) 123" or "1x Card Name"
    [GeneratedRegex(@"^(\d+)x?\s+(.+?)(?:\s+\(([A-Za-z0-9]+)\)\s+(\S+))?$")]
    private static partial Regex DecklistLineRegex();

    // Known section headers to skip (Moxfield/Archidekt text export)
    private static readonly HashSet<string> SectionHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Deck", "Sideboard", "Commander", "Companion", "Maybeboard", "Considering",
        "Main", "Mainboard", "Main Deck", "Tokens", "Attractions", "Stickers", "Contraptions"
    };

    public (string DeckName, List<DecklistEntry> Entries) ParseDecklistText(string text)
    {
        var entries = new Dictionary<string, DecklistEntry>(StringComparer.OrdinalIgnoreCase);
        var regex = DecklistLineRegex();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                continue;

            if (SectionHeaders.Contains(line))
                continue;

            var match = regex.Match(line);
            if (!match.Success)
                continue;

            var qty = int.Parse(match.Groups[1].Value);
            var name = match.Groups[2].Value.Trim();
            var setCode = match.Groups[3].Success ? match.Groups[3].Value.ToUpperInvariant() : null;
            var collectorNumber = match.Groups[4].Success ? match.Groups[4].Value : null;

            var key = name.ToUpperInvariant();
            if (entries.TryGetValue(key, out var existing))
                entries[key] = existing with { Quantity = existing.Quantity + qty };
            else
                entries[key] = new DecklistEntry(qty, name, setCode, collectorNumber);
        }

        return ("Pasted Decklist", entries.Values.ToList());
    }

    public async Task<(string DeckName, List<DecklistEntry> Entries)?> FetchDecklistAsync(string url)
    {
        var (source, deckId) = ParseUrl(url);
        if (source is null || deckId is null)
            return null;

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");
        client.Timeout = TimeSpan.FromSeconds(10);

        try
        {
            return source switch
            {
                "Moxfield" => await FetchMoxfieldAsync(client, deckId),
                "Archidekt" => await FetchArchidektAsync(client, deckId),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static (string? Source, string? DeckId) ParseUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return (null, null);

        var host = uri.Host.Replace("www.", "");
        var segments = uri.AbsolutePath.Trim('/').Split('/');

        if (host.Contains("moxfield.com") && segments.Length >= 2 && segments[0] == "decks")
            return ("Moxfield", segments[1]);

        if (host.Contains("archidekt.com") && segments.Length >= 2 && segments[0] == "decks")
            return ("Archidekt", segments[1]);

        return (null, null);
    }

    private static async Task<(string DeckName, List<DecklistEntry> Entries)?> FetchMoxfieldAsync(
        HttpClient client, string deckId)
    {
        var response = await client.GetAsync($"https://api2.moxfield.com/v2/decks/all/{deckId}");
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        var deckName = root.GetProperty("name").GetString() ?? "Moxfield Deck";

        var entries = new List<DecklistEntry>();

        // Moxfield stores cards in board objects: mainboard, sideboard, commanders, companions
        foreach (var boardName in new[] { "mainboard", "sideboard", "commanders", "companions" })
        {
            if (!root.TryGetProperty(boardName, out var board) || board.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var cardProp in board.EnumerateObject())
            {
                var cardObj = cardProp.Value;
                var qty = cardObj.GetProperty("quantity").GetInt32();
                var card = cardObj.GetProperty("card");
                var name = card.GetProperty("name").GetString() ?? "";
                var setCode = card.GetProperty("set").GetString()?.ToUpperInvariant();
                var cn = card.GetProperty("cn").GetString();

                entries.Add(new DecklistEntry(qty, name, setCode, cn));
            }
        }

        return (deckName, entries);
    }

    private static async Task<(string DeckName, List<DecklistEntry> Entries)?> FetchArchidektAsync(
        HttpClient client, string deckId)
    {
        var response = await client.GetAsync($"https://archidekt.com/api/decks/{deckId}/");
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        var deckName = root.GetProperty("name").GetString() ?? "Archidekt Deck";

        var entries = new List<DecklistEntry>();

        if (root.TryGetProperty("cards", out var cards) && cards.ValueKind == JsonValueKind.Array)
        {
            foreach (var cardObj in cards.EnumerateArray())
            {
                var qty = cardObj.GetProperty("quantity").GetInt32();
                var card = cardObj.GetProperty("card");
                var edition = card.GetProperty("edition");

                var name = card.GetProperty("oracleCard").GetProperty("name").GetString() ?? "";
                var setCode = edition.GetProperty("editioncode").GetString()?.ToUpperInvariant();
                var cn = card.TryGetProperty("collectorNumber", out var cnProp)
                    ? cnProp.GetString() : null;

                entries.Add(new DecklistEntry(qty, name, setCode, cn));
            }
        }

        return (deckName, entries);
    }

    public DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries)
    {
        throw new NotImplementedException(); // Implemented in Task 3
    }
}
