using OmniCard.Shared.Collection;
namespace OmniCard.Shared.Inventory;

/// <summary>Filter criteria for <see cref="IAnalyticsService.GetMovements"/>. All
/// filters are optional (null means "no restriction"); results are always ordered newest-first
/// and capped at <see cref="Take"/>.</summary>
public record MovementFilter(
    MovementType? Type = null,
    DateTime? Since = null,
    string? ProductQuery = null,
    int Take = 500);
