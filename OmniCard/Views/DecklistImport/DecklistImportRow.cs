using OmniCard.Models;

namespace OmniCard.Views.DecklistImport;

/// <summary>One parsed decklist line plus its resolution result, shown in the preview grid.</summary>
public sealed class DecklistImportRow
{
    public required int Quantity { get; init; }
    public required string Name { get; init; }
    public string? SetCode { get; init; }
    public string? CollectorNumber { get; init; }
    public CardMatch? Match { get; init; }

    public bool IsResolved => Match is not null;
    public string Status => Match is null ? "Unresolved" : "Resolved";
    public string ResolvedSet => Match?.SetCode ?? "";
    public string ResolvedNumber => Match?.CollectorNumber ?? "";
}
