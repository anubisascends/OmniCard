using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Security;
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

    // Built-in role names (seeded, IsSystem, not deletable).
    public const string AdministratorRole = "Administrator";
    public const string ViewerRole = "Viewer";
    public const string StaffRole = "Staff";

    /// <summary>Seed built-in roles and the built-in Admin account. Idempotent — safe on every startup,
    /// including upgrades of an existing DB that predates roles.</summary>
    public void EnsureSeeded()
    {
        using var db = factory.CreateDbContext();

        // Roles are seeded independently of users so an existing (pre-roles) DB still gets them.
        var existingRoles = db.Roles.Select(r => r.Name).ToHashSet();
        void SeedRole(string name, IEnumerable<string> perms)
        {
            if (existingRoles.Contains(name)) return;
            db.Roles.Add(new Role { Name = name, IsSystem = true, Permissions = perms.OrderBy(p => p).ToList() });
        }
        SeedRole(AdministratorRole, Permissions.All);
        SeedRole(ViewerRole, Permissions.ViewOnly);
        SeedRole(StaffRole, StaffPermissions);
        db.SaveChanges();

        if (!db.Users.Any())
        {
            db.Users.Add(new User
            {
                Username = SystemUsername,
                PasswordHash = PasswordHasher.Hash(SystemDefaultPassword),
                IsSystem = true,
                IsAdmin = true,
            });
            db.SaveChanges();
        }
    }

    /// <summary>The "Staff" preset: all views plus everyday edit actions (no delete, no admin/config areas).</summary>
    private static readonly IReadOnlyList<string> StaffPermissions =
    [
        .. Permissions.ViewOnly,
        Permissions.ScanCommit,
        Permissions.CollectionEdit, Permissions.CollectionExport,
        Permissions.LocationsCreate, Permissions.LocationsEdit,
        Permissions.BinderEdit,
        Permissions.InventoryCreate, Permissions.InventoryEdit,
        Permissions.ListsCreate, Permissions.ListsEdit, Permissions.ListsCommit,
        Permissions.TradesCreate, Permissions.TradesFinalize,
        Permissions.ImportRun, Permissions.ExportRun,
        Permissions.SalesOrdersCreate, Permissions.SalesOrdersEdit, Permissions.SalesOrdersImport,
        Permissions.SalesCustomersCreate, Permissions.SalesCustomersEdit,
        Permissions.SalesListingsCreate, Permissions.SalesListingsEdit, Permissions.SalesListingsPick,
    ];

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

    /// <summary>Verify credentials; returns the user on success, null on unknown user or bad password.
    /// <paramref name="login"/> is the username or the account's email. An account waiting on a setup
    /// key has no usable password, so it always fails here.</summary>
    public async Task<User?> AuthenticateAsync(string login, string password)
    {
        using var db = factory.CreateDbContext();
        var user = await FindByLoginAsync(db, login, track: false);
        if (user is null || user.SetupKeyHash is not null || !PasswordHasher.Verify(password, user.PasswordHash))
            return null;
        return user;
    }

    /// <summary>What the sign-in screen should ask for after the user enters <paramref name="login"/>.
    /// Unknown logins report <see cref="SignInStep.Password"/> so the screen doesn't reveal which accounts exist.</summary>
    public async Task<SignInStep> GetSignInStepAsync(string login)
    {
        using var db = factory.CreateDbContext();
        var user = await FindByLoginAsync(db, login, track: false);
        if (user is null)
            return SignInStep.Password;
        if (user.SetupKeyHash is not null)
            return SignInStep.SetupKey;
        // No key and no password: the key was voided by too many wrong tries, so an admin must issue a new one.
        return string.IsNullOrEmpty(user.PasswordHash) ? SignInStep.Locked : SignInStep.Password;
    }

    /// <summary>Finish a first sign-in or forced reset: check the admin-issued setup key and set the new
    /// password. A wrong key counts against <see cref="SetupKeys.MaxFailedAttempts"/>; reaching it voids
    /// the key. Throws <see cref="InvalidOperationException"/> on a blank new password (before the key is
    /// checked, so a mistake there doesn't cost an attempt).</summary>
    public async Task<SetupKeyOutcome> CompleteSetupAsync(string login, string setupKey, string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword))
            throw new InvalidOperationException("New password is required.");

        using var db = factory.CreateDbContext();
        var user = await FindByLoginAsync(db, login, track: true);
        if (user is null)
            return new(SetupKeyResult.InvalidKey, null, 0);
        if (user.SetupKeyHash is null)
            return new(string.IsNullOrEmpty(user.PasswordHash) ? SetupKeyResult.Locked : SetupKeyResult.NotPending, null, 0);

        if (!SetupKeys.Verify(setupKey, user.SetupKeyHash))
        {
            user.SetupKeyFailedAttempts++;
            var left = SetupKeys.MaxFailedAttempts - user.SetupKeyFailedAttempts;
            if (left <= 0)
                ClearSetupKey(user);
            await db.SaveChangesAsync();
            return left <= 0 ? new(SetupKeyResult.Locked, null, 0) : new(SetupKeyResult.InvalidKey, null, left);
        }

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        ClearSetupKey(user);
        await db.SaveChangesAsync();
        return new(SetupKeyResult.Ok, user, 0);
    }

    /// <summary>Admin action: void the user's password and require them to choose a new one at their next
    /// sign-in, proving it's them with <paramref name="setupKey"/> (which the admin passes on). Returns
    /// false if the user isn't found; throws on an invalid key.</summary>
    public async Task<bool> RequirePasswordResetAsync(int id, string setupKey)
    {
        var key = SetupKeys.Validate(setupKey);
        using var db = factory.CreateDbContext();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return false;

        user.PasswordHash = "";
        IssueSetupKey(user, key);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Create a new (non-system) user. The account has no password yet: the user signs in the
    /// first time with <paramref name="setupKey"/> (given to them by the admin) and chooses one. Non-admins
    /// with no explicit role default to the "Viewer" role (view-only baseline). Throws
    /// <see cref="InvalidOperationException"/> on a duplicate/invalid name, email or key.</summary>
    public async Task<User> CreateAsync(string username, string setupKey, bool isAdmin = false,
        int? roleId = null, PermissionOverrides? overrides = null, string? email = null)
    {
        username = (username ?? "").Trim();
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("Username is required.");
        var key = SetupKeys.Validate(setupKey);
        var normalizedEmail = NormalizeEmail(email);

        using var db = factory.CreateDbContext();
        if (await db.Users.AnyAsync(u => u.Username == username))
            throw new InvalidOperationException($"A user named \"{username}\" already exists.");
        await EnsureEmailFreeAsync(db, normalizedEmail, exceptUserId: null);

        // Non-admins default to the Viewer role when none is specified (view-only baseline).
        if (!isAdmin && roleId is null)
            roleId = await db.Roles.Where(r => r.Name == ViewerRole).Select(r => (int?)r.Id).FirstOrDefaultAsync();

        var user = new User
        {
            Username = username,
            Email = normalizedEmail,
            PasswordHash = "",
            IsSystem = false,
            IsAdmin = isAdmin,
            RoleId = roleId,
            Overrides = Sanitize(overrides),
        };
        IssueSetupKey(user, key);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // Exact username first; an input containing "@" also tries the (lower-cased) email.
    private static async Task<User?> FindByLoginAsync(OmniCardDbContext db, string? login, bool track)
    {
        login = (login ?? "").Trim();
        if (login.Length == 0)
            return null;
        var users = track ? db.Users : db.Users.AsNoTracking();
        var user = await users.FirstOrDefaultAsync(u => u.Username == login);
        if (user is null && login.Contains('@'))
        {
            var email = login.ToLowerInvariant();
            user = await users.FirstOrDefaultAsync(u => u.Email == email);
        }
        return user;
    }

    private static void IssueSetupKey(User user, string key)
    {
        user.SetupKeyHash = SetupKeys.Hash(key);
        user.SetupKeyIssuedAt = DateTime.UtcNow;
        user.SetupKeyFailedAttempts = 0;
    }

    private static void ClearSetupKey(User user)
    {
        user.SetupKeyHash = null;
        user.SetupKeyIssuedAt = null;
        user.SetupKeyFailedAttempts = 0;
    }

    /// <summary>Blank becomes null; otherwise trimmed + lower-cased. Throws on something that isn't an address.</summary>
    private static string? NormalizeEmail(string? email)
    {
        email = (email ?? "").Trim();
        if (email.Length == 0)
            return null;
        if (email.Length > 256 || !System.Net.Mail.MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            throw new InvalidOperationException($"\"{email}\" isn't a valid email address.");
        return email.ToLowerInvariant();
    }

    private static async Task EnsureEmailFreeAsync(OmniCardDbContext db, string? email, int? exceptUserId)
    {
        if (email is not null && await db.Users.AnyAsync(u => u.Email == email && u.Id != exceptUserId))
            throw new InvalidOperationException($"Another user already uses the email \"{email}\".");
    }

    /// <summary>Update a user's email, role, per-user overrides, and admin flag (a blank email clears it).
    /// The system account stays admin (its flag can't be cleared). Returns the updated user, or null if not found.</summary>
    public async Task<User?> UpdateUserAsync(int id, int? roleId, PermissionOverrides? overrides, bool isAdmin, string? email)
    {
        var normalizedEmail = NormalizeEmail(email);
        using var db = factory.CreateDbContext();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return null;
        await EnsureEmailFreeAsync(db, normalizedEmail, exceptUserId: id);

        user.Email = normalizedEmail;
        user.RoleId = roleId;
        user.Overrides = Sanitize(overrides);
        // The built-in system account is always an admin and can't be demoted.
        user.IsAdmin = user.IsSystem || isAdmin;
        await db.SaveChangesAsync();
        return user;
    }

    // --- Roles ---

    public async Task<List<Role>> ListRolesAsync()
    {
        using var db = factory.CreateDbContext();
        return await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
    }

    /// <summary>Create a custom role. Throws on a blank/duplicate name.</summary>
    public async Task<Role> CreateRoleAsync(string name, IEnumerable<string>? permissions)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Role name is required.");

        using var db = factory.CreateDbContext();
        if (await db.Roles.AnyAsync(r => r.Name == name))
            throw new InvalidOperationException($"A role named \"{name}\" already exists.");

        var role = new Role { Name = name, IsSystem = false, Permissions = SanitizePermissions(permissions) };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role;
    }

    /// <summary>Rename / re-permission a role. System roles keep their name but permissions can be tuned.
    /// Returns the updated role, or null if not found.</summary>
    public async Task<Role?> UpdateRoleAsync(int id, string? name, IEnumerable<string>? permissions)
    {
        using var db = factory.CreateDbContext();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null)
            return null;

        if (!role.IsSystem && !string.IsNullOrWhiteSpace(name))
        {
            var trimmed = name.Trim();
            if (await db.Roles.AnyAsync(r => r.Id != id && r.Name == trimmed))
                throw new InvalidOperationException($"A role named \"{trimmed}\" already exists.");
            role.Name = trimmed;
        }
        role.Permissions = SanitizePermissions(permissions);
        await db.SaveChangesAsync();
        return role;
    }

    /// <summary>Delete a custom role and detach it from any users. System roles can't be deleted;
    /// returns false if not found or blocked.</summary>
    public async Task<bool> DeleteRoleAsync(int id)
    {
        using var db = factory.CreateDbContext();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null || role.IsSystem)
            return false;

        await db.Users.Where(u => u.RoleId == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.RoleId, (int?)null));
        db.Roles.Remove(role);
        await db.SaveChangesAsync();
        return true;
    }

    // Keep only known permission keys so stray/renamed keys can't accumulate in storage.
    private static List<string> SanitizePermissions(IEnumerable<string>? keys) =>
        (keys ?? []).Where(Permissions.IsValid).Distinct().OrderBy(k => k).ToList();

    private static PermissionOverrides Sanitize(PermissionOverrides? o) => new()
    {
        Grant = SanitizePermissions(o?.Grant),
        Deny = SanitizePermissions(o?.Deny),
    };

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

    /// <summary>Self-service email change for the Account page. Requires the current password because the
    /// email is a sign-in name. A blank email clears it. Returns false if the password doesn't match; throws
    /// <see cref="InvalidOperationException"/> on an invalid or taken email.</summary>
    public async Task<bool> ChangeOwnEmailAsync(int id, string currentPassword, string? email)
    {
        var normalizedEmail = NormalizeEmail(email);
        using var db = factory.CreateDbContext();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null || !PasswordHasher.Verify(currentPassword, user.PasswordHash))
            return false;
        await EnsureEmailFreeAsync(db, normalizedEmail, exceptUserId: id);

        user.Email = normalizedEmail;
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
}

/// <summary>What the sign-in screen asks for next: the password, the admin-issued setup key plus a new
/// password, or nothing (the key was voided; the user must ask an admin for a new one).</summary>
public enum SignInStep { Password, SetupKey, Locked }

public enum SetupKeyResult { Ok, InvalidKey, Locked, NotPending }

/// <summary>Result of <see cref="UserService.CompleteSetupAsync"/>. <see cref="User"/> is set only on
/// <see cref="SetupKeyResult.Ok"/>; <see cref="AttemptsLeft"/> only on <see cref="SetupKeyResult.InvalidKey"/>
/// for a real pending account.</summary>
public sealed record SetupKeyOutcome(SetupKeyResult Result, User? User, int AttemptsLeft);
