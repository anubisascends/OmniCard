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

    /// <summary>Create a new (non-system) user. Non-admins with no explicit role default to the "Viewer"
    /// role (view-only baseline). Throws <see cref="InvalidOperationException"/> on a duplicate/invalid name.</summary>
    public async Task<User> CreateAsync(string username, string password, bool isAdmin = false,
        int? roleId = null, PermissionOverrides? overrides = null)
    {
        username = (username ?? "").Trim();
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("Username is required.");
        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Password is required.");

        using var db = factory.CreateDbContext();
        if (await db.Users.AnyAsync(u => u.Username == username))
            throw new InvalidOperationException($"A user named \"{username}\" already exists.");

        // Non-admins default to the Viewer role when none is specified (view-only baseline).
        if (!isAdmin && roleId is null)
            roleId = await db.Roles.Where(r => r.Name == ViewerRole).Select(r => (int?)r.Id).FirstOrDefaultAsync();

        var user = new User
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            IsSystem = false,
            IsAdmin = isAdmin,
            RoleId = roleId,
            Overrides = Sanitize(overrides),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Update a user's role, per-user overrides, and admin flag. The system account stays admin
    /// (its flag can't be cleared). Returns the updated user, or null if not found.</summary>
    public async Task<User?> UpdateUserAsync(int id, int? roleId, PermissionOverrides? overrides, bool isAdmin)
    {
        using var db = factory.CreateDbContext();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return null;

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
