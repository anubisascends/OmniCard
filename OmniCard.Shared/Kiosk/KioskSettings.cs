namespace OmniCard.Shared.Kiosk;

/// <summary>Admin settings for the customer kiosk (<c>kiosk-settings.json</c> in the data directory).</summary>
public sealed class KioskSettings
{
    public const int MinIdleTimeoutMinutes = 2;
    public const int MaxIdleTimeoutMinutes = 240;
    public const int MaxWelcomeMessageLength = 300;

    /// <summary>When off, enrolled kiosks show a "closed" screen and take no new sessions.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The storage locations whose cards customers can browse. Empty = nothing to browse.</summary>
    public List<int> LocationIds { get; set; } = [];

    /// <summary>A browsing session with no activity for this long is released (its holds freed).</summary>
    public int IdleTimeoutMinutes { get; set; } = 15;

    /// <summary>Show market prices to customers.</summary>
    public bool ShowPrices { get; set; } = true;

    /// <summary>Optional text on the kiosk's welcome screen.</summary>
    public string? WelcomeMessage { get; set; }
}
