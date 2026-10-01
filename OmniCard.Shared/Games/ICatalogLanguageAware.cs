using OmniCard.Shared.Matching;

namespace OmniCard.Shared.Games;

/// <summary>
/// Implemented by the <see cref="ICardGameService"/>s whose upstream source can supply non-English
/// printings (MTG via Scryfall <c>all_cards</c>, One Piece via poneglyph <c>lang=</c>, Pokémon Japan via
/// TCGCSV category 85). The catalog-refresh job sets <see cref="CatalogLanguages"/> from the user's
/// saved selection before each run; a bulk download then fetches exactly those languages and prunes
/// non-English rows for languages that were un-ticked.
/// </summary>
public interface ICatalogLanguageAware
{
    /// <summary>Languages this game's source can download, English first (see <see cref="CardLanguages"/>).</summary>
    IReadOnlyList<string> DownloadableLanguages { get; }

    /// <summary>The languages the next download should fetch. English is always included.</summary>
    IReadOnlyCollection<string> CatalogLanguages { get; set; }

    /// <summary>The catalog language of <paramref name="gameCardId"/>, or null when the id is unknown.</summary>
    string? GetCardLanguage(string gameCardId);

    /// <summary>The same printing (set + collector number, same art variant where the source tracks
    /// it) in <paramref name="language"/>, or null when the catalog has no such row — e.g. that language
    /// wasn't downloaded, or (Pokémon Japan) the language's printings are entirely different sets.</summary>
    CardMatch? FindLanguageVariant(string gameCardId, string language);
}
