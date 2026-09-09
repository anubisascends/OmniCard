namespace OmniCard.Shared.Sales;

/// <summary>One field-level change captured in an <see cref="OrderEdit"/>'s ChangesJson.</summary>
public sealed record OrderEditChange(string Field, string? OldValue, string? NewValue);
