using System.Text.RegularExpressions;
using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Games;

/// <summary>
/// The card-language vocabulary shared by every game. Codes follow Scryfall's scheme (lower-case,
/// <c>zhs</c>/<c>zht</c> for Simplified/Traditional Chinese) so MTG catalog rows need no translation;
/// other games map onto the same codes. English is <see cref="English"/> and is always implied — an
/// owned lot with a null language is English.
/// <para>
/// The built-in lists below cover every language known when they were written. A catalog source can
/// still serve a language added after that (Scryfall added Klingon, Dwarven, Quenya… for one-off
/// products): the catalog refresh reports such codes (<see cref="ICatalogLanguageAware.DiscoveredLanguages"/>),
/// they are persisted, and <see cref="RegisterDiscovered"/> adds them at startup and after each refresh,
/// so they can be downloaded, tagged and searched without a code change.
/// </para>
/// </summary>
public static partial class CardLanguages
{
    public const string English = "en";

    // Every language a physical card can be tagged with, in display order. MTG's one-off languages
    // (Phyrexian onward) follow the regular print languages.
    private static readonly string[] BuiltIn =
        ["en", "ja", "de", "fr", "it", "es", "pt", "ko", "ru", "zhs", "zht", "th", "id",
         "ph", "he", "la", "grc", "ar", "sa", "qya", "dw", "tlh"];

    // MTG: every language Scryfall serves (all_cards, 2026-11): the eleven regular print languages plus
    // the one-off ones — Phyrexian, Hebrew/Arabic/Latin/Sanskrit/Ancient Greek (old promos and
    // Assassin's Creed), Quenya (Lord of the Rings), Dwarven (Hobbit), Klingon (Star Trek).
    private static readonly string[] MtgLanguages =
        ["en", "ja", "de", "fr", "it", "es", "pt", "ko", "ru", "zhs", "zht",
         "ph", "he", "la", "grc", "ar", "sa", "qya", "dw", "tlh"];

    // The languages each game has actually been printed in (what a user can tag an owned copy as),
    // whether or not a catalog for that language can be downloaded.
    private static readonly Dictionary<CardGame, string[]> PrintedBuiltIn = new()
    {
        [CardGame.Mtg] = MtgLanguages,
        [CardGame.OnePiece] = ["en", "ja", "fr", "zhs", "ko"],
        [CardGame.Riftbound] = ["en", "zhs", "fr", "ko"],
        [CardGame.Pokemon] = ["en", "ja", "de", "fr", "it", "es", "pt", "ko", "zhs", "zht", "th", "id"],
        [CardGame.YuGiOh] = ["en", "ja", "de", "fr", "it", "es", "pt", "ko", "zhs"],
        [CardGame.FinalFantasy] = ["en", "ja", "de", "fr", "it", "es"],
    };

    // The languages each game's catalog source can download (see ICatalogLanguageAware):
    //   MTG       — Scryfall all_cards, every language it serves.
    //   Pokémon   — TCGCSV category 85 "Pokemon Japan" (separate sets, real TCGplayer prices).
    // One Piece / Riftbound / Yu-Gi-Oh! / FFTCG sources (TCGCSV) are English-only; owned copies can still
    // be tagged with any printed language, and Yu-Gi-Oh! region codes are read from the scan
    // (YugiohService). One Piece keeps any Japanese/French rows downloaded earlier from poneglyph.
    private static readonly Dictionary<CardGame, string[]> DownloadableBuiltIn = new()
    {
        [CardGame.Mtg] = MtgLanguages,
        [CardGame.Pokemon] = ["en", "ja"],
    };

    // Built-in lists plus registered discoveries. Replaced whole under the lock, read lock-free.
    private static readonly object RegistryLock = new();
    private static volatile Snapshot _current = new(BuiltIn, PrintedBuiltIn, DownloadableBuiltIn);

    private sealed class Snapshot(
        string[] all,
        IReadOnlyDictionary<CardGame, string[]> printed,
        IReadOnlyDictionary<CardGame, string[]> downloadable)
    {
        public string[] All { get; } = all;
        public IReadOnlyDictionary<CardGame, string[]> Printed { get; } = printed;
        public IReadOnlyDictionary<CardGame, string[]> Downloadable { get; } = downloadable;
        public HashSet<string> Known { get; } = [.. all];
    }

    /// <summary>Every language a physical card can be tagged with, in display order (built-in languages
    /// first, then any discovered ones).</summary>
    public static IReadOnlyList<string> All => _current.All;

    /// <summary>Languages an owned copy of <paramref name="game"/> can be tagged with (English first).</summary>
    public static IReadOnlyList<string> ForGame(CardGame game) =>
        _current.Printed.TryGetValue(game, out var langs) ? langs : [English];

    /// <summary>Languages <paramref name="game"/>'s catalog can download (English first; English-only
    /// games return just English).</summary>
    public static IReadOnlyList<string> DownloadableFor(CardGame game) =>
        _current.Downloadable.TryGetValue(game, out var langs) ? langs : [English];

    /// <summary>True when <paramref name="code"/> is a well-formed language code (2–8 lower-case
    /// letters, Scryfall style). Used to vet codes a catalog source reports before trusting them.</summary>
    public static bool IsValidCode(string? code) => code is not null && CodePattern().IsMatch(code);

    [GeneratedRegex("^[a-z]{2,8}$")]
    private static partial Regex CodePattern();

    /// <summary>
    /// Adds languages <paramref name="game"/>'s catalog source serves that the built-in lists don't know
    /// (see <see cref="ICatalogLanguageAware.DiscoveredLanguages"/>). They become taggable, downloadable
    /// (when the game's source downloads more than English) and recognized by <see cref="Normalize"/>.
    /// Invalid codes are ignored. Returns the codes that were actually new for the game.
    /// </summary>
    public static IReadOnlyList<string> RegisterDiscovered(CardGame game, IEnumerable<string> codes)
    {
        lock (RegistryLock)
        {
            var current = _current;
            var printed = ForGame(game);
            var added = codes.Select(c => c?.Trim().ToLowerInvariant() ?? "")
                .Where(IsValidCode)
                .Where(c => !printed.Contains(c))
                .Distinct()
                .ToList();
            if (added.Count == 0) return [];

            var all = current.All.Concat(added.Where(c => !current.Known.Contains(c))).ToArray();
            var printedMap = new Dictionary<CardGame, string[]>(current.Printed) { [game] = [.. printed, .. added] };
            var downloadMap = new Dictionary<CardGame, string[]>(current.Downloadable);
            if (downloadMap.TryGetValue(game, out var downloadable))
                downloadMap[game] = [.. downloadable, .. added.Where(c => !downloadable.Contains(c))];
            _current = new Snapshot(all, printedMap, downloadMap);
            return added;
        }
    }

    /// <summary>Sanitizes a language selection for <paramref name="game"/>'s catalog: normalized, limited
    /// to <see cref="DownloadableFor"/>, deduplicated, English always first.</summary>
    public static IReadOnlyList<string> SanitizeCatalogSelection(CardGame game, IEnumerable<string>? languages)
    {
        var allowed = DownloadableFor(game);
        var chosen = new HashSet<string>((languages ?? []).Select(Normalize).OfType<string>()) { English };
        return allowed.Where(chosen.Contains).ToList();
    }

    /// <summary>
    /// Normalizes a user/API/OCR language token to a canonical code, or null when unrecognized.
    /// Accepts canonical codes (built-in or discovered) plus the printed/ISO variants seen on cards and
    /// in exports (MTG prints "JP", "SP", "KR", "CS", "CT"; Yu-Gi-Oh! uses "SP"/"SC"; Manabox exports
    /// "japanese"/"en"…). Blank ⇒ null.
    /// </summary>
    public static string? Normalize(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var t = token.Trim().ToLowerInvariant().Replace('_', '-');
        return t switch
        {
            "en" or "eng" or "english" or "en-us" or "en-gb" or "ae" => "en",
            "ja" or "jp" or "jpn" or "japanese" => "ja",
            "de" or "ger" or "deu" or "german" or "g" => "de",
            "fr" or "fra" or "fre" or "french" or "f" => "fr",
            "it" or "ita" or "italian" or "i" => "it",
            "es" or "sp" or "spa" or "spanish" or "s" or "es-mx" => "es",
            "pt" or "por" or "portuguese" or "pt-br" or "p" => "pt",
            "ko" or "kr" or "kor" or "korean" => "ko",
            "ru" or "rus" or "russian" => "ru",
            "zhs" or "zh" or "cs" or "sc" or "zh-cn" or "zh-hans" or "chinese" or "schinese" or "simplified chinese" or "chinese simplified" => "zhs",
            "zht" or "ct" or "tc" or "zh-tw" or "zh-hant" or "tchinese" or "traditional chinese" or "chinese traditional" => "zht",
            "th" or "tha" or "thai" => "th",
            "id" or "ind" or "indonesian" => "id",
            "ph" or "phyrexian" => "ph",
            "he" or "heb" or "hebrew" => "he",
            "la" or "lat" or "latin" => "la",
            "grc" or "ancient greek" or "greek" => "grc",
            "ar" or "ara" or "arabic" => "ar",
            "sa" or "san" or "sanskrit" => "sa",
            "qya" or "quenya" => "qya",
            "dw" or "dwarven" or "khuzdul" => "dw",
            "tlh" or "klingon" => "tlh",
            _ => _current.Known.Contains(t) ? t : null,
        };
    }

    /// <summary>True when <paramref name="code"/> (already normalized, null ⇒ English) is English.</summary>
    public static bool IsEnglish(string? code) => code is null || code == English;

    /// <summary>The value to persist on a lot: null for English (the implied default) so existing rows
    /// and English copies stay uniform, otherwise the canonical code. Unknown tokens persist as null.</summary>
    public static string? ToStored(string? token)
    {
        var code = Normalize(token);
        return code is null || code == English ? null : code;
    }
}
