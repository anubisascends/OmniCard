namespace OmniCard.Shared.Sales;

/// <summary>Per-order aggregate of its lines, for kanban card display.</summary>
public record OrderLineSummary(int OrderId, int ItemCount, decimal Total);
