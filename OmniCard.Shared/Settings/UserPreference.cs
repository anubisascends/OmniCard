namespace OmniCard.Shared.Settings;

/// <summary>
/// One remembered UI setting of one user (for example how tall the Scan page's compare images are), so it
/// follows the user from browser to browser. The value is opaque here: the SPA owns its shape (usually a
/// small JSON object). One row per user + key; rows go with their user (cascade).
/// </summary>
public class UserPreference
{
    public const int MaxKeyLength = 100;
    public const int MaxValueLength = 4000;

    public int UserId { get; set; }

    /// <summary>Which setting, e.g. <c>scan.review</c>.</summary>
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";

    public DateTime UpdatedAt { get; set; }
}
