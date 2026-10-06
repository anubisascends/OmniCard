using OmniCard.Api.Contracts;
using OmniCard.Shared.Cards;

namespace OmniCard.Web.Services;

/// <summary>Matches one card image against a game's catalog. Implemented by
/// <see cref="WebScanMatchingService"/>; the seam lets the background batch processor be tested
/// without the imaging/OCR stack.</summary>
public interface IScanMatcher
{
    Task<ScanMatchDto> MatchAsync(byte[] imageBytes, CardGame game, bool isFoil,
        IReadOnlyCollection<string>? setCodes = null, string? language = null, CancellationToken ct = default);
}
