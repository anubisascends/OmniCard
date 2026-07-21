namespace OmniCard.Models;

public class CompanyProfile
{
    public string? Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    /// <summary>Path to the logo image, relative to the data directory.</summary>
    public string? LogoPath { get; set; }
}
