using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Sets;

/// <summary>Builds a set-completion checklist (every printing + owned quantity) and the printable
/// want-list report of the cards not yet owned. Backs the Sets tab.</summary>
public interface ISetChecklistService
{
    /// <summary>Every printing in the set annotated with owned quantity and prices, sorted by
    /// collector number.</summary>
    Task<SetChecklist> BuildAsync(CardGame game, string setCode);

    /// <summary>Lower-cased codes of the sets the user owns at least one (non-traded) single in —
    /// backs the Sets tab's "Owned sets only" filter.</summary>
    IReadOnlySet<string> GetOwnedSetCodes(CardGame game);

    /// <summary>Distills a checklist into the printable want-list (unowned cards only).</summary>
    SetChecklistReport BuildWantListReport(SetChecklist checklist);
}
