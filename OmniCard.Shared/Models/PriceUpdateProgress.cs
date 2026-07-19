namespace OmniCard.Models;

/// <summary>Progress for a background price refresh. SetCode/Total are populated for
/// per-set sources (One Piece) and may be null/0 for bulk sources (MTG).</summary>
public sealed record PriceUpdateProgress(
    CardGame Game,
    string? SetCode,
    int Completed,
    int Total,
    string Message);
