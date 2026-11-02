namespace OmniCard.Shared.Dashboard;

/// <summary>
/// A saved Dashboard: which widgets it shows, where, and how each one is configured. The layout itself
/// is opaque here (<see cref="LayoutJson"/>); the web layer owns its shape and sanitizes it on save.
///
/// <para><b>Scope.</b> A row is either one user's own dashboard (<see cref="UserId"/> set) or everyone's
/// default (<see cref="UserId"/> null), set by an administrator. A user without a dashboard of their own
/// sees everyone's default; with no default saved either, the built-in layout (the classic totals +
/// breakdown tables) is shown. One row per user, and at most one default. Personal rows go with their
/// user (cascade).</para>
/// </summary>
public class DashboardLayout
{
    public int Id { get; set; }

    /// <summary>Owner; null = everyone's default.</summary>
    public int? UserId { get; set; }

    /// <summary>The widgets, serialized by the web layer.</summary>
    public string LayoutJson { get; set; } = "{}";

    public DateTime UpdatedAt { get; set; }

    public bool IsDefault => UserId is null;
}
