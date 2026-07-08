using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IMismatchLogService
{
    Task LogMismatchAsync(CardMatch oldMatch, CardMatch newMatch, ScannedCard scannedCard);
}
