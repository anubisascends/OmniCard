namespace OmniCard.Models;

public class EbaySettings
{
    public string AppId { get; set; } = "";
    public string CertId { get; set; } = "";
    public string DevId { get; set; } = "";
    public string RuName { get; set; } = "";
    public string AcceptUrl { get; set; } = "";
    public string Environment { get; set; } = "sandbox";

    public string AuthBaseUrl => Environment == "production"
        ? "https://auth.ebay.com"
        : "https://auth.sandbox.ebay.com";

    public string ApiBaseUrl => Environment == "production"
        ? "https://api.ebay.com"
        : "https://api.sandbox.ebay.com";
}
