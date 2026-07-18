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
/// Not stacked: scanned art only.
/// Stacked: downloaded art first, then the stack representative's scanned art.
/// Empty result means no art is available -> the view shows a placeholder.
/// </summary>
public static class CardArtCandidateResolver
{
    public static IReadOnlyList<CardArtCandidate> Resolve(CollectionCard card, bool isStacked)
    {
        var candidates = new List<CardArtCandidate>();

        if (isStacked)
        {
            if (!string.IsNullOrEmpty(card.ImageUri))
                candidates.Add(new CardArtCandidate(CardArtKind.Downloaded, card.ImageUri));
            if (!string.IsNullOrEmpty(card.ScanImagePath))
                candidates.Add(new CardArtCandidate(CardArtKind.Scan, card.ScanImagePath));
        }
        else
        {
            if (!string.IsNullOrEmpty(card.ScanImagePath))
                candidates.Add(new CardArtCandidate(CardArtKind.Scan, card.ScanImagePath));
        }

        return candidates;
    }
}
