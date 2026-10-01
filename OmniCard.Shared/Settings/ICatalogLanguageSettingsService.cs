using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Settings;

/// <summary>Reads and persists which card languages to download per game's catalog
/// (Settings ▸ Catalog data). English is always included and can't be removed.</summary>
public interface ICatalogLanguageSettingsService
{
    /// <summary>The languages selected for <paramref name="game"/> — always contains English, and only
    /// codes the game's source can actually download.</summary>
    IReadOnlyList<string> GetLanguages(CardGame game);

    /// <summary>Persists the selection for <paramref name="game"/>. Codes are normalized, filtered to the
    /// game's downloadable languages, and English is added if missing. Returns the saved selection.</summary>
    IReadOnlyList<string> SetLanguages(CardGame game, IEnumerable<string> languages);
}
