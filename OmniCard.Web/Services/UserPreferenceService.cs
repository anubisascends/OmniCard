using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Settings;

namespace OmniCard.Web.Services;

/// <summary>
/// Remembered UI settings of the signed-in user (<see cref="UserPreference"/>): a small key → value bag the
/// SPA reads once and writes as the user changes things, so the settings follow the user between browsers.
/// Values are opaque strings (the SPA stores JSON); keys are short dotted names like <c>scan.review</c>.
/// </summary>
public sealed partial class UserPreferenceService(IDbContextFactory<OmniCardDbContext> factory, TimeProvider time)
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex KeyPattern();

    /// <summary>True when <paramref name="key"/> is a usable preference key.</summary>
    public static bool IsValidKey(string? key) =>
        !string.IsNullOrEmpty(key) && key.Length <= UserPreference.MaxKeyLength && KeyPattern().IsMatch(key);

    public async Task<Dictionary<string, string>> GetAllAsync(int userId)
    {
        using var db = factory.CreateDbContext();
        return await db.UserPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.Key, p => p.Value);
    }

    /// <summary>Store one preference (insert or replace). A null or empty value removes it.</summary>
    public async Task SetAsync(int userId, string key, string? value)
    {
        if (!IsValidKey(key))
            throw new ArgumentException("Invalid preference key.", nameof(key));
        if (value is { Length: > UserPreference.MaxValueLength })
            throw new ArgumentException($"A preference value can be at most {UserPreference.MaxValueLength} characters.", nameof(value));

        // Two quick saves of the same key (a resize then a speed change) can race on the insert: retry once
        // as an update when the other one won.
        for (var attempt = 0; ; attempt++)
        {
            using var db = factory.CreateDbContext();
            var row = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId && p.Key == key);
            if (string.IsNullOrEmpty(value))
            {
                if (row is null) return;
                db.UserPreferences.Remove(row);
            }
            else if (row is null)
            {
                db.UserPreferences.Add(new UserPreference
                {
                    UserId = userId,
                    Key = key,
                    Value = value,
                    UpdatedAt = time.GetUtcNow().UtcDateTime,
                });
            }
            else
            {
                row.Value = value;
                row.UpdatedAt = time.GetUtcNow().UtcDateTime;
            }

            try
            {
                await db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // Lost the race: go round once more against the row the other save wrote (or removed).
            }
        }
    }
}
