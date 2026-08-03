using OmniCard.Models;

namespace OmniCard.Interfaces;

/// <summary>Builds a per-location price sheet from the unified inventory store (singles and
/// sealed product alike), and reports which games/categories are present so the caller can
/// scope a price refresh before building.</summary>
public interface IPriceSheetService
{
    PriceSheetReport BuildReport(int containerId, string containerName);
    IReadOnlyCollection<CardGame> GetGamesPresent(int containerId);
    bool HasSealedProduct(int containerId);
    bool HasAnyProduct(int containerId);
}
