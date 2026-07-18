namespace OmniCard.Models;

/// <summary>Which art source a candidate refers to.</summary>
public enum CardArtKind
{
    Scan,
    Downloaded
}

/// <summary>An ordered art source to try. Value is a scan path (relative to the data dir) or a download URI.</summary>
public readonly record struct CardArtCandidate(CardArtKind Kind, string Value);

/// <summary>
/// Decides which art sources to try, in order, for a collection card.
/// Always prefers the downloaded (API) art, then falls back to the scanned image.
/// Empty result means no art is available -> the view shows a placeholder.
/// (Scanned images can still be reviewed in the card editor via double-click.)
/// </summary>
public static class CardArtCandidateResolver
{
    public static IReadOnlyList<CardArtCandidate> Resolve(CollectionCard card)
    {
        var candidates = new List<CardArtCandidate>();

        if (!string.IsNullOrEmpty(card.ImageUri))
            candidates.Add(new CardArtCandidate(CardArtKind.Downloaded, card.ImageUri));
        if (!string.IsNullOrEmpty(card.ScanImagePath))
            candidates.Add(new CardArtCandidate(CardArtKind.Scan, card.ScanImagePath));

        return candidates;
    }
}
