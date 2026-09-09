using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Settings;

namespace OmniCard.Web.Services;

// Takes IDbContextFactory (the writable SQL Server factory satisfies it in production; tests can
// supply an in-memory SQLite factory) rather than the concrete writable type.

/// <summary>
/// User accounts + authentication for the SPA. Passwords are stored only as salted PBKDF2 hashes
/// (<see cref="PasswordHasher"/>). Reads/writes go through the writable unified-store factory.
///
/// The built-in <c>Admin</c> account (default password <c>admin</c>) is seeded on first run by
/// <see cref="EnsureSeeded"/>; it can't be deleted and is always an admin.
/// </summary>
public sealed class UserService(IDbContextFactory<OmniCardDbContext> factory)
{
    public const string SystemUsername = "Admin";
    public const string SystemDefaultPassword = "admin";

    /// <summary>Create the built-in Admin account when the users table is empty. Idempotent.</summary>
    public void EnsureSeeded()
    {
        using var db = factory.CreateDbContext();
        if (db.Users.Any())
            return;

        db.Users.Add(new User
        {
            Username = SystemUsername,
            PasswordHash = PasswordHasher.Hash(SystemDefaultPassword),
            IsSystem = true,
            IsAdmin = true,
        });
        db.SaveChanges();
    }

    public async Task<List<User>> ListAsync()
    {
        using var db = factory.CreateDbContext();
        return await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync();
    }

    public async Task<User?> FindByIdAsync(int id)
    {
        using var db = factory.CreateDbContext();
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
    }

    /// <summary>Verify credentials; returns the user on success, null on unknown user or bad password.</summary>
    public async Task<User?> AuthenticateAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;
        using var db = factory.CreateDbContext();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username);
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
            return null;
        return user;
    }

    /// <summary>Create a new (non-system) user. Throws <see cref="InvalidOperationException"/> on a duplicate/invalid name.</summary>
    public async Task<User> CreateAsync(string username, string password, bool isAdmin = false)
    {
        username = (username ?? "").Trim();
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("Username is required.");
        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Password is required.");

        using var db = factory.CreateDbContext();
        if (await db.Users.AnyAsync(u => u.Username == username))
            throw new InvalidOperationException($"A user named \"{username}\" already exists.");

        var user = new User
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            IsSystem = false,
            IsAdmin = isAdmin,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Delete a user. The system account can't be deleted; returns false if not found or blocked.</summary>
    public async Task<bool> DeleteAsync(int id)
    {
        using var db = factory.CreateDbContext();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null || user.IsSystem)
            return false;
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Self-service change: requires the current password. Returns false if it doesn't match.</summary>
    public async Task<bool> ChangePasswordAsync(int id, string currentPassword, string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword))
            throw new InvalidOperationException("New password is required.");

        using var db = factory.CreateDbContext();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null || !PasswordHasher.Verify(currentPassword, user.PasswordHash))
            return false;

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Admin reset: set a user's password without knowing the old one. Returns false if not found.</summary>
    public async Task<bool> ResetPasswordAsync(int id, string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword))
            throw new InvalidOperationException("New password is required.");

        using var db = factory.CreateDbContext();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return false;

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await db.SaveChangesAsync();
        return true;
    }
}
