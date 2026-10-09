using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace OmniCard.Web.Services;

/// <summary>
/// Rules for the one-time setup keys an admin hands a user (for a new account or a forced password
/// reset). A key is letters and digits only and is compared case-insensitively, so it can be read out
/// over the phone. Only its PBKDF2 hash is stored (see <see cref="PasswordHasher"/>).
/// </summary>
public static partial class SetupKeys
{
    public const int MinLength = 6;
    public const int MaxLength = 64;

    /// <summary>Wrong keys allowed against one issued key before it's voided and the admin must issue another.</summary>
    public const int MaxFailedAttempts = 5;

    // No 0/O, 1/I/L: generated keys are meant to be read aloud or copied by hand.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    [GeneratedRegex("^[A-Za-z0-9]+$")]
    private static partial Regex AlphanumericRegex();

    /// <summary>Trimmed, upper-cased form that is hashed and compared.</summary>
    public static string Normalize(string? key) => (key ?? "").Trim().ToUpperInvariant();

    /// <summary>Throws <see cref="InvalidOperationException"/> unless the key is 6–64 letters/digits.</summary>
    public static string Validate(string? key)
    {
        var k = Normalize(key);
        if (k.Length < MinLength || k.Length > MaxLength || !AlphanumericRegex().IsMatch(k))
            throw new InvalidOperationException(
                $"The setup key must be {MinLength}–{MaxLength} letters or digits (no spaces or symbols).");
        return k;
    }

    public static string Hash(string key) => PasswordHasher.Hash(Normalize(key));

    public static bool Verify(string? key, string? hash) => PasswordHasher.Verify(Normalize(key), hash);

    /// <summary>A random key from an unambiguous alphabet (used by tests; the SPA generates its own).</summary>
    public static string Generate(int length = 10) =>
        new(Enumerable.Range(0, length).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
}
