namespace OmniCard.Models;

public class EbaySellerPolicy
{
    public string PolicyId { get; set; } = "";
    public string Name { get; set; } = "";
    public string PolicyType { get; set; } = "";

    public override string ToString() => Name;
}
