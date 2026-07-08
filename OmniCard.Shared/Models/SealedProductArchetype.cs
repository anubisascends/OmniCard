namespace OmniCard.Models;

/// <summary>
/// Defines the default structure for a sealed product type:
/// how it's named, what it contains, and its tier in the product hierarchy.
/// </summary>
public record SealedProductArchetype(
    string NamePattern,
    List<ArchetypeContent> DefaultContents,
    ArchetypeTier Tier
);

public record ArchetypeContent(int Quantity, SealedProductType ChildType);
