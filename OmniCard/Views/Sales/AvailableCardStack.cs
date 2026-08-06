using OmniCard.Models;

namespace OmniCard.Views.Sales;

/// <summary>Groups active listings that represent the same distinct item (name/set/condition/
/// foil/price) so the "Add card" picker shows one row with a count instead of one row per lot.</summary>
public class AvailableCardStack(ActiveListing first)
{
    public string Name { get; } = first.Name;
    public string SetName { get; } = first.SetName;
    public string? Condition { get; } = first.Condition;
    public bool IsFoil { get; } = first.IsFoil;
    public decimal ListedPrice { get; } = first.ListedPrice;

    public List<ActiveListing> Listings { get; } = [first];

    public int Count => Listings.Count;

    /// <summary>Text shown in the editable ComboBox once a stack is selected — without this, an
    /// editable ComboBox falls back to <see cref="ToString"/> for the edit box (it only uses
    /// ItemTemplate for the closed/dropdown display), so this is also bound via
    /// TextSearch.TextPath and mirrored by the ToString override below.</summary>
    public string DisplayText => $"{Name} — {SetName} ({Condition}) ${ListedPrice:F2} ×{Count}";

    public override string ToString() => DisplayText;
}
