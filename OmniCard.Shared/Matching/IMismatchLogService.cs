using OmniCard.Shared.Scanning;

namespace OmniCard.Shared.Matching;

public interface IMismatchLogService
{
    Task LogMismatchAsync(CardMatch oldMatch, CardMatch newMatch, ScannedCard scannedCard);
}
