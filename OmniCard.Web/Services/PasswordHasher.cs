using System.Security.Cryptography;

namespace OmniCard.Web.Services;

/// <summary>
/// Salted PBKDF2 password hashing so stored credentials can't be trivially unpacked if the database
/// leaks. Each password gets a fresh random salt; the encoded value carries everything needed to
/// verify (and to transparently upgrade should the parameters change later):
/// <c>pbkdf2$sha256$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;</c>.
/// Verification uses a constant-time comparison.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;      // 128-bit salt
    private const int HashSize = 32;      // 256-bit derived key
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>Hash a plaintext password into a self-describing encoded string safe to store.</summary>
    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);
        return $"pbkdf2$sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Constant-time verification of a plaintext password against a stored encoded hash.</summary>
    public static bool Verify(string password, string? encoded)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(encoded))
            return false;

        var parts = encoded.Split('$');
        // pbkdf2 $ sha256 $ iterations $ salt $ hash
        if (parts.Length != 5 || parts[0] != "pbkdf2" || parts[1] != "sha256")
            return false;
        if (!int.TryParse(parts[2], out var iterations) || iterations <= 0)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
