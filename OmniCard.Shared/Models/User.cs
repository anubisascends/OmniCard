namespace OmniCard.Models;

/// <summary>
/// An application user account. Authentication is username + password (the password is stored only
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

    /// <summary>Encoded PBKDF2 hash (algorithm$iterations$salt$hash) — see the web PasswordHasher.</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>True for the built-in Admin account: it can't be deleted and is always an admin.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Full permissions (reserved for the future permission system). Always true for the system account.</summary>
    public bool IsAdmin { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
