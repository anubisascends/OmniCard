using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Games;

/// <summary>
/// The card-language vocabulary shared by every game. Codes follow Scryfall's scheme (lower-case,
/// <c>zhs</c>/<c>zht</c> for Simplified/Traditional Chinese) so MTG catalog rows need no translation;
/// other games map onto the same codes. English is <see cref="English"/> and is always implied — an
/// owned lot with a null language is English.
/// </summary>
public static class CardLanguages
{
    public const string English = "en";

    /// <summary>Every language a physical card can be tagged with, in display order.</summary>
    public static readonly IReadOnlyList<string> All =
        ["en", "ja", "de", "fr", "it", "es", "pt", "ko", "ru", "zhs", "zht", "th", "id", "ph"];

    // The languages each game has actually been printed in (what a user can tag an owned copy as),
    // whether or not a catalog for that language can be downloaded.
    private static readonly Dictionary<CardGame, string[]> Printed = new()
    {
        [CardGame.Mtg] = ["en", "ja", "de", "fr", "it", "es", "pt", "ko", "ru", "zhs", "zht", "ph"],
        [CardGame.OnePiece] = ["en", "ja", "fr", "zhs", "ko"],
        [CardGame.Riftbound] = ["en", "zhs", "fr", "ko"],
        [CardGame.Pokemon] = ["en", "ja", "de", "fr", "it", "es", "pt", "ko", "zhs", "zht", "th", "id"],
        [CardGame.YuGiOh] = ["en", "ja", "de", "fr", "it", "es", "pt", "ko", "zhs"],
        [CardGame.FinalFantasy] = ["en", "ja", "de", "fr", "it", "es"],
    };

    // The languages each game's catalog source can download (see ICatalogLanguageAware):
    //   MTG       — Scryfall all_cards, every printed language.
    //   One Piece — api.poneglyph.one `lang=` (en/ja/fr; its zh has no data).
    //   Pokémon   — TCGCSV category 85 "Pokemon Japan" (separate sets, real TCGplayer prices).
    // Riftbound / Yu-Gi-Oh! / FFTCG sources are English-only; owned copies can still be tagged with any
    // printed language, and Yu-Gi-Oh! region codes are read from the scan (YugiohService).
    private static readonly Dictionary<CardGame, string[]> Downloadable = new()
    {
        [CardGame.Mtg] = ["en", "ja", "de", "fr", "it", "es", "pt", "ko", "ru", "zhs", "zht", "ph"],
        [CardGame.OnePiece] = ["en", "ja", "fr"],
        [CardGame.Pokemon] = ["en", "ja"],
    };

    /// <summary>Languages an owned copy of <paramref name="game"/> can be tagged with (English first).</summary>
    public static IReadOnlyList<string> ForGame(CardGame game) =>
        Printed.TryGetValue(game, out var langs) ? langs : [English];

    /// <summary>Languages <paramref name="game"/>'s catalog can download (English first; English-only
    /// games return just English).</summary>
    public static IReadOnlyList<string> DownloadableFor(CardGame game) =>
        Downloadable.TryGetValue(game, out var langs) ? langs : [English];

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
    /// Accepts canonical codes plus the printed/ISO variants seen on cards and in exports
    /// (MTG prints "JP", "SP", "KR", "CS", "CT"; Yu-Gi-Oh! uses "SP"/"SC"; Manabox exports
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
            "zhs" or "zh" or "cs" or "sc" or "zh-cn" or "zh-hans" or "chinese" or "simplified chinese" or "chinese simplified" => "zhs",
            "zht" or "ct" or "tc" or "zh-tw" or "zh-hant" or "traditional chinese" or "chinese traditional" => "zht",
            "th" or "tha" or "thai" => "th",
            "id" or "ind" or "indonesian" => "id",
            "ph" or "phyrexian" => "ph",
            _ => null,
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
