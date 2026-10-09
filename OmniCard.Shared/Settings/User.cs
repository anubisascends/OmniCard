namespace OmniCard.Shared.Settings;

/// <summary>
/// An application user account. Authentication is username (or email) + password (the password is stored only
/// as a salted PBKDF2 hash in <see cref="PasswordHash"/> — never in plaintext). One built-in
/// <see cref="IsSystem"/> account named "Admin" is seeded on first run (default password "admin").
///
/// <para><see cref="IsAdmin"/> is carried now but not yet used to restrict anything beyond user
/// management; it exists so per-user permissions can be layered on later without a schema change.
/// The system account is always an admin and cannot be deleted or demoted.</para>
/// </summary>
public class User
{
    public int Id { get; set; }

    public string Username { get; set; } = "";

    /// <summary>Optional email address (stored trimmed + lower-case, unique when set). The user can type
    /// it in place of <see cref="Username"/> at sign-in.</summary>
    public string? Email { get; set; }

    /// <summary>Encoded PBKDF2 hash (algorithm$iterations$salt$hash) — see the web PasswordHasher.
    /// Empty while the account is waiting for its first password (see <see cref="SetupKeyHash"/>).</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>PBKDF2 hash of the one-time setup key an admin issued (on account creation or a forced
    /// password reset). While set, the account can't sign in with a password: the user must enter this key
    /// and choose a new password. Cleared once used, or when too many wrong keys are tried.</summary>
    public string? SetupKeyHash { get; set; }

    /// <summary>When the current setup key was issued (null when none is pending).</summary>
    public DateTime? SetupKeyIssuedAt { get; set; }

    /// <summary>Wrong setup keys tried against the current key; reaching the limit voids the key.</summary>
    public int SetupKeyFailedAttempts { get; set; }

    /// <summary>True for the built-in Admin account: it can't be deleted and is always an admin.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Full permissions — unlocks every feature, bypassing role/overrides. Always true for the system account.</summary>
    public bool IsAdmin { get; set; }

    /// <summary>The role whose permissions form this user's baseline. Null means no role (no baseline).
    /// New non-admin users default to the seeded "Viewer" role. Ignored for admins.</summary>
    public int? RoleId { get; set; }

    /// <summary>Per-user grant/deny adjustments layered on top of the role (persisted as a JSON column).</summary>
    public PermissionOverrides Overrides { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-user permission adjustments applied on top of the assigned role:
/// effective = role.Permissions ∪ <see cref="Grant"/> − <see cref="Deny"/>. Deny wins over grant.
/// </summary>
public sealed class PermissionOverrides
{
    /// <summary>Permission keys explicitly granted to this user beyond their role.</summary>
    public List<string> Grant { get; set; } = [];

    /// <summary>Permission keys explicitly removed from this user, even if the role grants them.</summary>
    public List<string> Deny { get; set; } = [];
}
