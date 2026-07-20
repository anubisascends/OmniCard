namespace OmniCard.Models;

/// <summary>A single inventory ledger entry, projected for display in the movement history
/// browser — joins <see cref="InventoryMovement"/> to its owning <see cref="Product"/>.</summary>
public record MovementView(
    DateTime Timestamp,
    MovementType Type,
    string ProductName,
    CardGame? Game,
    int Quantity,
    decimal? UnitValue,
    string? Note);
