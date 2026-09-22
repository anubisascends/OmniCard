using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Security;

namespace OmniCard.Web.Services;

/// <summary>
/// Resolves and caches each user's effective permission set from live DB state, so an admin's change
/// to a user or role takes effect on that user's very next request — no sign-out required. The auth
/// cookie carries identity only; this service (not the cookie) is the authority on what a user can do.
///
/// <para>Effective set = <c>role.Permissions ∪ overrides.Grant − overrides.Deny</c>, or every
/// permission (<see cref="Permissions.All"/>) when the user is an admin/system account.</para>
///
/// <para>Results are cached per user id and invalidated on any change: <see cref="Invalidate"/> after
/// a user edit, <see cref="InvalidateAll"/> after a role edit (roles are shared, so a role change can
/// affect many users; role edits are rare admin actions, so clearing the whole cache is cheap enough).</para>
/// </summary>
public sealed class PermissionService(IDbContextFactory<OmniCardDbContext> factory)
{
    private readonly ConcurrentDictionary<int, IReadOnlySet<string>> _cache = new();

    /// <summary>The user's effective permission keys (cached; loaded from the DB on a miss).</summary>
    public async Task<IReadOnlySet<string>> GetEffectiveAsync(int userId)
    {
        if (_cache.TryGetValue(userId, out var cached))
            return cached;
        var resolved = await ResolveAsync(userId);
        _cache[userId] = resolved;
        return resolved;
    }

    /// <summary>True when the user currently holds <paramref name="permission"/> (admins always do).</summary>
    public async Task<bool> HasPermissionAsync(int userId, string permission)
    {
        var set = await GetEffectiveAsync(userId);
        return set.Contains(permission);
    }

    /// <summary>Drop one user's cached set — call after editing that user (role/overrides/admin/delete).</summary>
    public void Invalidate(int userId) => _cache.TryRemove(userId, out _);

    /// <summary>Drop every cached set — call after a role is edited or deleted (it may affect many users).</summary>
    public void InvalidateAll() => _cache.Clear();

    private async Task<IReadOnlySet<string>> ResolveAsync(int userId)
    {
        using var db = factory.CreateDbContext();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return EmptySet;
        if (user.IsAdmin || user.IsSystem)
            return Permissions.All;

        var effective = new HashSet<string>(StringComparer.Ordinal);
        if (user.RoleId is int roleId)
        {
            var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId);
            if (role is not null)
                foreach (var key in role.Permissions)
                    effective.Add(key);
        }
        foreach (var key in user.Overrides.Grant)
            effective.Add(key);
        foreach (var key in user.Overrides.Deny)
            effective.Remove(key);
        return effective;
    }

    private static readonly IReadOnlySet<string> EmptySet =
        new HashSet<string>(StringComparer.Ordinal);
}
