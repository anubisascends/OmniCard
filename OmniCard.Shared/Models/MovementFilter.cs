namespace OmniCard.Models;

/// <summary>Filter criteria for <see cref="Interfaces.IAnalyticsService.GetMovements"/>. All
/// filters are optional (null means "no restriction"); results are always ordered newest-first
/// and capped at <see cref="Take"/>.</summary>
public record MovementFilter(
    MovementType? Type = null,
    DateTime? Since = null,
    string? ProductQuery = null,
    int Take = 500);
