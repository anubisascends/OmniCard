using OmniCard.Models;

namespace OmniCard.Collection;

/// <summary>Applies a tag toggle to pre-commit scanned cards. Pure in-memory mutation of
/// <see cref="ScannedCard.Tags"/> — no DB access, since scans have no lot id until commit.</summary>
public static class ScanTagToggle
{
    /// <summary>Applies <paramref name="tagName"/> to every card (case-insensitive; no duplicate
    /// added if a matching tag is already present) when <paramref name="apply"/> is true, or
    /// removes it (case-insensitive; no-op if absent) when <paramref name="apply"/> is false.</summary>
    public static void Apply(IEnumerable<ScannedCard> cards, string tagName, bool apply)
    {
        foreach (var card in cards)
        {
            if (apply)
            {
                if (!card.Tags.Contains(tagName, StringComparer.OrdinalIgnoreCase))
                    card.Tags.Add(tagName);
            }
            else
            {
                var existing = card.Tags.FirstOrDefault(t => string.Equals(t, tagName, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                    card.Tags.Remove(existing);
            }
        }
    }

    /// <summary>Trims <paramref name="name"/>; if the result is non-empty, applies it to every
    /// card via <see cref="Apply"/> and returns the trimmed name, otherwise returns null and
    /// touches no cards.</summary>
    public static string? CreateAndApply(IEnumerable<ScannedCard> cards, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return null;

        Apply(cards, trimmed, apply: true);
        return trimmed;
    }
}
