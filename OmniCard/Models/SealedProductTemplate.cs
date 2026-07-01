namespace OmniCard.Models;

public class SealedProductTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? SetCode { get; set; }
    public string? Upc { get; set; }
    public SealedProductType ProductType { get; set; }
    public List<SealedProductContents> Contents { get; set; } = [];
}
