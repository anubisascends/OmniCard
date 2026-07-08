namespace OmniCard.Models;

/// <summary>
/// Defines one line of what a template contains when cracked.
/// Either references a specific child template (ChildTemplateId) or just a product type.
/// </summary>
public class SealedProductContents
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public SealedProductTemplate Template { get; set; } = null!;

    public int Quantity { get; set; } = 1;
    public SealedProductType ChildProductType { get; set; }

    /// <summary>Optional reference to a specific child template. Null means generic (user picks at crack time).</summary>
    public int? ChildTemplateId { get; set; }
    public SealedProductTemplate? ChildTemplate { get; set; }
}
