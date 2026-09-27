namespace OmniCard.Shared.Lists;

/// <param name="Finish">The foil finish the source specifies (e.g. "Foil", "Etched" from a Moxfield/Archidekt
/// URL); null means non-foil / unspecified. Only the URL-to-location import consumes it.</param>
public record DecklistEntry(int Quantity, string CardName, string? SetCode, string? CollectorNumber, string? Finish = null);
