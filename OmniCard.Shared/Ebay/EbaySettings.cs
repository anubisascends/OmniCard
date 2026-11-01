namespace OmniCard.Shared.Ebay;

public class EbaySettings
{
    public string AppId { get; set; } = "";
    public string CertId { get; set; } = "";
    public string DevId { get; set; } = "";
    public string RuName { get; set; } = "";
    public string Environment { get; set; } = "sandbox";

    /// <summary>Base URL of the eBay token broker (the Cloudflare Worker in <c>tools/ebay-broker</c>).
    /// When set and <see cref="CertId"/> is blank, token calls go through the broker, which holds the
    /// keyset's secret, and eBay's sign-in redirect returns via the broker's <c>/callback</c>.</summary>
    public string BrokerUrl { get; set; } = "";

    /// <summary>True when this server has no CertId of its own and relies on the broker. A configured
    /// CertId (an install with its own keyset) always wins.</summary>
    public bool UsesBroker => string.IsNullOrWhiteSpace(CertId) && !string.IsNullOrWhiteSpace(BrokerUrl);

    public string AuthBaseUrl => Environment == "production"
        ? "https://auth.ebay.com"
        : "https://auth.sandbox.ebay.com";

    public string ApiBaseUrl => Environment == "production"
        ? "https://api.ebay.com"
        : "https://api.sandbox.ebay.com";

    /// <summary>Public buyer-facing site base, for building "view item" links (e.g. {WebBaseUrl}/itm/{id}).</summary>
    public string WebBaseUrl => Environment == "production"
        ? "https://www.ebay.com"
        : "https://www.sandbox.ebay.com";
}
