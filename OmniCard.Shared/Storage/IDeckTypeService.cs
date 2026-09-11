using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Storage;

/// <summary>Per-game deck-format reference data: seeded built-ins plus user-created custom types.
/// Backs the deck-box game/type pickers and the deck-legality rules.</summary>
public interface IDeckTypeService
{
    /// <summary>Seed the built-in deck types when missing. Idempotent — keyed on each built-in's
    /// stable <see cref="DeckType.BuiltInKey"/>, so it never duplicates and never overwrites a
    /// user-renamed built-in. Safe to call on every startup.</summary>
    void EnsureSeeded();

    /// <summary>All deck types for a game (built-in + custom), ordered for display.</summary>
    List<DeckType> GetForGame(CardGame game);

    DeckType? GetById(int id);

    /// <summary>Create a custom deck type. Throws <see cref="InvalidOperationException"/> if the name
    /// is blank or already used for that game.</summary>
    DeckType Create(DeckType deckType);

    /// <summary>Update a deck type's name/rules (built-ins may be renamed/edited too). Load-then-patch.</summary>
    void Update(int id, DeckType changes);

    /// <summary>Delete a deck type. Any deck boxes referencing it have their reference cleared
    /// (FK SetNull).</summary>
    void Delete(int id);
}
