using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Views;

namespace OmniCard.Web.Services;

public enum SavedViewErrorKind { NotFound, Forbidden, BadRequest, Conflict }

/// <summary>A saved-view rule was broken; the controller maps <see cref="Kind"/> to an HTTP status.</summary>
public sealed class SavedViewException(SavedViewErrorKind kind, string message) : Exception(message)
{
    public SavedViewErrorKind Kind { get; } = kind;
}

/// <summary>
/// Saved views of the Collection and Location pages (see <see cref="SavedView"/>) and which one each
/// page opens with. This service owns the data rules — who sees and edits which view, name uniqueness,
/// and default resolution; the controller layers section permissions and site access on top.
///
/// <para><b>Visibility.</b> On a page, a user sees their own views plus the shared ones, for the
/// selected game or for any game. A Location page also offers the shared "all locations" views.</para>
///
/// <para><b>Defaults.</b> A page opens with the first of: the user's default for the selected game,
/// the user's any-game default, everyone's default for the selected game, everyone's any-game default
/// — and on a Location page, then everyone's all-locations defaults the same way. None → the built-in
/// layout. Only administrators edit shared views or set everyone's default.</para>
/// </summary>
public sealed class SavedViewService(IDbContextFactory<OmniCardDbContext> factory, TimeProvider time)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly int[] PageSizes = [25, 50, 100];
    private const int MaxQueryLength = 2000;
    private const int MaxColumns = 32;
    private const int MaxFieldLength = 64;

    // --- reads --------------------------------------------------------------------------------------

    /// <summary>The views offered on <paramref name="page"/> (Collection or one Location) while
    /// <paramref name="gameKey"/> is selected (a game, or <see cref="SavedView.AllGames"/>), and the one
    /// it should open with.</summary>
    public SavedViewListDto List(int userId, bool isAdmin, SavedViewPage page, int? containerId, string gameKey)
    {
        RequirePageScope(page, containerId);
        using var db = factory.CreateDbContext();
        var views = VisibleOn(db, userId, page, containerId, gameKey)
            .AsNoTracking()
            .ToList()
            .OrderBy(v => v.IsShared)
            .ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var defaults = DefaultCandidates(db, userId, page, containerId, gameKey);
        var visibleIds = views.Select(v => v.Id).ToHashSet();
        var defaultId = defaults.Select(d => (int?)d.SavedViewId).FirstOrDefault(id => visibleIds.Contains(id!.Value));
        var mine = defaults.Where(d => d.UserId == userId).Select(d => d.SavedViewId).ToHashSet();
        var everyone = defaults.Where(d => d.UserId is null).Select(d => d.SavedViewId).ToHashSet();

        return new SavedViewListDto
        {
            Views = views.Select(v => ToDto(v, userId, isAdmin, mine.Contains(v.Id), everyone.Contains(v.Id))).ToList(),
            DefaultViewId = defaultId,
        };
    }

    /// <summary>One view the user can see (their own or a shared one), for opening a view link.</summary>
    public SavedViewDto Get(int id, int userId, bool isAdmin)
    {
        using var db = factory.CreateDbContext();
        var view = LoadVisible(db, id, userId);
        var defaults = db.SavedViewDefaults.AsNoTracking()
            .Where(d => d.SavedViewId == id && (d.UserId == userId || d.UserId == null))
            .ToList();
        return ToDto(view, userId, isAdmin,
            isMyDefault: defaults.Any(d => d.UserId == userId),
            isEveryoneDefault: defaults.Any(d => d.UserId is null));
    }

    /// <summary>The page a view belongs to (for the caller's permission/site checks), or null when the
    /// view doesn't exist.</summary>
    public (SavedViewPage Page, int? ContainerId)? ScopeOf(int id)
    {
        using var db = factory.CreateDbContext();
        var view = db.SavedViews.AsNoTracking()
            .Where(v => v.Id == id)
            .Select(v => new { v.Page, v.ContainerId })
            .FirstOrDefault();
        return view is null ? null : (view.Page, view.ContainerId);
    }

    // --- writes -------------------------------------------------------------------------------------

    public SavedViewDto Create(int userId, bool isAdmin, CreateSavedViewRequest request)
    {
        var page = ParsePage(request.Page);
        var containerId = page == SavedViewPage.Location ? request.ContainerId : null;
        if (page == SavedViewPage.Location && containerId is null)
            throw new SavedViewException(SavedViewErrorKind.BadRequest, "A location view needs a location.");
        if (request.Shared && !isAdmin)
            throw new SavedViewException(SavedViewErrorKind.Forbidden, "Only administrators can share views.");
        if (page == SavedViewPage.AllLocations && !request.Shared)
            throw new SavedViewException(SavedViewErrorKind.BadRequest, "All-locations views must be shared.");

        var name = RequireName(request.Name);
        var gameKey = ParseViewGame(request.Game);
        int? owner = request.Shared ? null : userId;

        using var db = factory.CreateDbContext();
        if (containerId is int cid && !db.StorageContainers.Any(c => c.Id == cid))
            throw new SavedViewException(SavedViewErrorKind.NotFound, "Location not found.");
        EnsureNameFree(db, owner, page, containerId, gameKey, name, excludeId: null);

        var now = time.GetUtcNow().UtcDateTime;
        var view = new SavedView
        {
            UserId = owner,
            Page = page,
            ContainerId = containerId,
            GameKey = gameKey,
            Name = name,
            StateJson = Serialize(request.State),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.SavedViews.Add(view);
        db.SaveChanges();
        return ToDto(view, userId, isAdmin, false, false);
    }

    /// <summary>Rename and/or save the current layout over a view the user may edit.</summary>
    public SavedViewDto Update(int id, int userId, bool isAdmin, UpdateSavedViewRequest request)
    {
        using var db = factory.CreateDbContext();
        var view = LoadEditable(db, id, userId, isAdmin);
        if (request.Name is not null)
        {
            var name = RequireName(request.Name);
            EnsureNameFree(db, view.UserId, view.Page, view.ContainerId, view.GameKey, name, excludeId: id);
            view.Name = name;
        }
        if (request.State is not null)
            view.StateJson = Serialize(request.State);
        view.UpdatedAt = time.GetUtcNow().UtcDateTime;
        db.SaveChanges();
        return Get(id, userId, isAdmin);
    }

    /// <summary>Delete a view the user may edit. Every default pointing at it goes with it, so those
    /// pages fall back to the next default in line.</summary>
    public void Delete(int id, int userId, bool isAdmin)
    {
        using var db = factory.CreateDbContext();
        var view = LoadEditable(db, id, userId, isAdmin);
        db.SavedViewDefaults.RemoveRange(db.SavedViewDefaults.Where(d => d.SavedViewId == id));
        db.SavedViews.Remove(view);
        db.SaveChanges();
    }

    /// <summary>
    /// Make a view the default. <paramref name="everyone"/> (administrators, shared views only) sets it
    /// for every user without a default of their own, on the view's own page — every location for an
    /// all-locations view. Otherwise it becomes the user's own default on the page they're on
    /// (<paramref name="page"/> / <paramref name="containerId"/>), which must be a page the view is
    /// offered on. Either way the default applies to the view's game key.
    ///
    /// <para>An any-game default ranks below a default for one game, so making an any-game view the
    /// default also drops the same slot's default for <paramref name="selectedGameKey"/> (the game on
    /// screen) — otherwise the page would keep opening the other view. Defaults for other games stay.</para>
    /// </summary>
    public void SetDefault(int id, int userId, bool isAdmin, bool everyone, SavedViewPage page, int? containerId,
        string? selectedGameKey = null)
    {
        using var db = factory.CreateDbContext();
        var view = LoadVisible(db, id, userId);
        var slot = DefaultSlot(view, userId, isAdmin, everyone, page, containerId);
        Upsert(db, slot.UserId, slot.Page, slot.ContainerId, view.GameKey, view.Id);
        if (view.GameKey == SavedView.AnyGame && selectedGameKey is { Length: > 0 } shadowing)
            db.SavedViewDefaults.RemoveRange(db.SavedViewDefaults.Where(d =>
                d.UserId == slot.UserId && d.Page == slot.Page && d.ContainerId == slot.ContainerId
                && d.GameKey == shadowing));
        db.SaveChanges();
    }

    /// <summary>Stop a view being the default (the user's own, or everyone's) on that page.</summary>
    public void ClearDefault(int id, int userId, bool isAdmin, bool everyone, SavedViewPage page, int? containerId)
    {
        using var db = factory.CreateDbContext();
        var view = LoadVisible(db, id, userId);
        var slot = DefaultSlot(view, userId, isAdmin, everyone, page, containerId);
        db.SavedViewDefaults.RemoveRange(db.SavedViewDefaults.Where(d =>
            d.SavedViewId == id && d.UserId == slot.UserId && d.Page == slot.Page
            && d.ContainerId == slot.ContainerId && d.GameKey == view.GameKey));
        db.SaveChanges();
    }

    /// <summary>
    /// Copy a Location view to other locations, keeping its owner (shared stays shared), name and game.
    /// A view with the same name there is overwritten. With <paramref name="setDefault"/> the copy becomes
    /// the owner's default there (everyone's, for a shared view). Unknown locations and the view's own
    /// location are skipped. Returns how many locations received the view.
    /// </summary>
    public int Copy(int id, int userId, bool isAdmin, IReadOnlyCollection<int> containerIds, bool setDefault)
    {
        using var db = factory.CreateDbContext();
        var source = LoadEditable(db, id, userId, isAdmin);
        if (source.Page != SavedViewPage.Location)
            throw new SavedViewException(SavedViewErrorKind.BadRequest, "Only a location's views can be copied to other locations.");

        var targets = containerIds.Distinct().Where(c => c != source.ContainerId).ToList();
        var existing = db.StorageContainers.Where(c => targets.Contains(c.Id)).Select(c => c.Id).ToHashSet();
        targets = targets.Where(existing.Contains).ToList();
        if (targets.Count == 0)
            return 0;

        var now = time.GetUtcNow().UtcDateTime;
        var sameNamed = db.SavedViews
            .Where(v => v.UserId == source.UserId && v.Page == SavedViewPage.Location
                        && v.GameKey == source.GameKey && targets.Contains(v.ContainerId!.Value))
            .ToList()
            .Where(v => string.Equals(v.Name, source.Name, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(v => v.ContainerId!.Value);

        var copies = new List<SavedView>();
        foreach (var target in targets)
        {
            if (sameNamed.TryGetValue(target, out var copy))
            {
                copy.StateJson = source.StateJson;
                copy.UpdatedAt = now;
            }
            else
            {
                copy = new SavedView
                {
                    UserId = source.UserId,
                    Page = SavedViewPage.Location,
                    ContainerId = target,
                    GameKey = source.GameKey,
                    Name = source.Name,
                    StateJson = source.StateJson,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.SavedViews.Add(copy);
            }
            copies.Add(copy);
        }
        db.SaveChanges();

        if (setDefault)
        {
            foreach (var copy in copies)
                Upsert(db, source.UserId, SavedViewPage.Location, copy.ContainerId, source.GameKey, copy.Id);
            db.SaveChanges();
        }
        return copies.Count;
    }

    /// <summary>Remove a deleted user's defaults (their own views cascade with the user).</summary>
    public void RemoveUser(int userId)
    {
        using var db = factory.CreateDbContext();
        db.SavedViewDefaults.Where(d => d.UserId == userId).ExecuteDelete();
    }

    /// <summary>Remove a deleted location's defaults (its views cascade with the location).</summary>
    public void RemoveLocation(int containerId)
    {
        using var db = factory.CreateDbContext();
        db.SavedViewDefaults.Where(d => d.Page == SavedViewPage.Location && d.ContainerId == containerId)
            .ExecuteDelete();
    }

    // --- parsing (shared with the controller) -------------------------------------------------------

    public static SavedViewPage ParsePage(string? page) =>
        Enum.TryParse<SavedViewPage>(page, ignoreCase: true, out var p) && Enum.IsDefined(p)
            ? p
            : throw new SavedViewException(SavedViewErrorKind.BadRequest, $"Unknown page '{page}'.");

    /// <summary>The game key for the game selected on a page: a game, or All Games when none.</summary>
    public static string ParseSelectedGame(string? game) =>
        string.IsNullOrWhiteSpace(game) ? SavedView.AllGames : ParseViewGame(game);

    /// <summary>The game key a view is saved for: a game, All Games, or any game when none.</summary>
    public static string ParseViewGame(string? game)
    {
        if (string.IsNullOrWhiteSpace(game))
            return SavedView.AnyGame;
        if (string.Equals(game, SavedView.AllGames, StringComparison.OrdinalIgnoreCase))
            return SavedView.AllGames;
        return Enum.TryParse<CardGame>(game, ignoreCase: true, out var g) && Enum.IsDefined(g)
            ? g.ToString()
            : throw new SavedViewException(SavedViewErrorKind.BadRequest, $"Unknown game '{game}'.");
    }

    // --- helpers ------------------------------------------------------------------------------------

    private static void RequirePageScope(SavedViewPage page, int? containerId)
    {
        if (page == SavedViewPage.AllLocations)
            throw new SavedViewException(SavedViewErrorKind.BadRequest, "Pick a location page.");
        if (page == SavedViewPage.Location && containerId is null)
            throw new SavedViewException(SavedViewErrorKind.BadRequest, "A location page needs a location.");
    }

    private static IQueryable<SavedView> VisibleOn(
        OmniCardDbContext db, int userId, SavedViewPage page, int? containerId, string gameKey)
    {
        var onLocation = page == SavedViewPage.Location;
        return db.SavedViews.Where(v =>
            (v.UserId == userId || v.UserId == null)
            && (v.GameKey == gameKey || v.GameKey == SavedView.AnyGame)
            && ((v.Page == page && v.ContainerId == containerId)
                || (onLocation && v.Page == SavedViewPage.AllLocations && v.UserId == null)));
    }

    /// <summary>The default rows that apply to this page and game, in priority order.</summary>
    private static List<SavedViewDefault> DefaultCandidates(
        OmniCardDbContext db, int userId, SavedViewPage page, int? containerId, string gameKey)
    {
        var onLocation = page == SavedViewPage.Location;
        var rows = db.SavedViewDefaults.AsNoTracking()
            .Where(d => (d.UserId == userId || d.UserId == null)
                        && (d.GameKey == gameKey || d.GameKey == SavedView.AnyGame)
                        && ((d.Page == page && d.ContainerId == containerId)
                            || (onLocation && d.UserId == null && d.Page == SavedViewPage.AllLocations)))
            .ToList();
        return rows
            .OrderBy(d => d.UserId is null ? 1 : 0)
            .ThenBy(d => d.Page == SavedViewPage.AllLocations ? 1 : 0)
            .ThenBy(d => d.GameKey == SavedView.AnyGame ? 1 : 0)
            .ToList();
    }

    private static SavedView LoadVisible(OmniCardDbContext db, int id, int userId) =>
        db.SavedViews.FirstOrDefault(v => v.Id == id && (v.UserId == userId || v.UserId == null))
        ?? throw new SavedViewException(SavedViewErrorKind.NotFound, "View not found.");

    private static SavedView LoadEditable(OmniCardDbContext db, int id, int userId, bool isAdmin)
    {
        var view = LoadVisible(db, id, userId);
        if (view.IsShared && !isAdmin)
            throw new SavedViewException(SavedViewErrorKind.Forbidden, "Only administrators can change shared views.");
        return view;
    }

    private static (int? UserId, SavedViewPage Page, int? ContainerId) DefaultSlot(
        SavedView view, int userId, bool isAdmin, bool everyone, SavedViewPage page, int? containerId)
    {
        if (everyone)
        {
            if (!isAdmin)
                throw new SavedViewException(SavedViewErrorKind.Forbidden, "Only administrators can set everyone's default.");
            if (!view.IsShared)
                throw new SavedViewException(SavedViewErrorKind.BadRequest, "Only a shared view can be everyone's default.");
            return (null, view.Page, view.ContainerId);
        }

        RequirePageScope(page, containerId);
        var offeredHere = (view.Page == page && view.ContainerId == (page == SavedViewPage.Location ? containerId : null))
                          || (view.Page == SavedViewPage.AllLocations && page == SavedViewPage.Location);
        if (!offeredHere)
            throw new SavedViewException(SavedViewErrorKind.BadRequest, "That view isn't offered on this page.");
        return (userId, page, page == SavedViewPage.Location ? containerId : null);
    }

    private static void Upsert(OmniCardDbContext db, int? userId, SavedViewPage page, int? containerId, string gameKey, int viewId)
    {
        var row = db.SavedViewDefaults.FirstOrDefault(d =>
            d.UserId == userId && d.Page == page && d.ContainerId == containerId && d.GameKey == gameKey);
        if (row is null)
            db.SavedViewDefaults.Add(new SavedViewDefault
            {
                UserId = userId, Page = page, ContainerId = containerId, GameKey = gameKey, SavedViewId = viewId,
            });
        else
            row.SavedViewId = viewId;
    }

    private static string RequireName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
            throw new SavedViewException(SavedViewErrorKind.BadRequest, "A view needs a name.");
        if (trimmed.Length > SavedView.MaxNameLength)
            throw new SavedViewException(SavedViewErrorKind.BadRequest,
                $"View names can be at most {SavedView.MaxNameLength} characters.");
        return trimmed;
    }

    /// <summary>Names are unique per owner, page and game (case-insensitive).</summary>
    private static void EnsureNameFree(
        OmniCardDbContext db, int? owner, SavedViewPage page, int? containerId, string gameKey, string name, int? excludeId)
    {
        var taken = db.SavedViews
            .Where(v => v.UserId == owner && v.Page == page && v.ContainerId == containerId
                        && v.GameKey == gameKey && v.Id != excludeId)
            .Select(v => v.Name)
            .AsEnumerable()
            .Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (taken)
            throw new SavedViewException(SavedViewErrorKind.Conflict, $"There's already a view called \"{name}\" here.");
    }

    private static SavedViewDto ToDto(SavedView v, int userId, bool isAdmin, bool isMyDefault, bool isEveryoneDefault) => new()
    {
        Id = v.Id,
        Name = v.Name,
        Page = v.Page.ToString(),
        ContainerId = v.ContainerId,
        Game = v.GameKey == SavedView.AnyGame ? null : v.GameKey,
        Shared = v.IsShared,
        CanEdit = v.UserId == userId || (v.IsShared && isAdmin),
        IsMyDefault = isMyDefault,
        IsEveryoneDefault = isEveryoneDefault,
        State = Deserialize(v.StateJson),
    };

    private static string Serialize(SavedViewStateDto? state) =>
        JsonSerializer.Serialize(Sanitize(state ?? new SavedViewStateDto()), JsonOptions);

    private static SavedViewStateDto Deserialize(string json)
    {
        try
        {
            return Sanitize(JsonSerializer.Deserialize<SavedViewStateDto>(json, JsonOptions) ?? new SavedViewStateDto());
        }
        catch (JsonException)
        {
            return new SavedViewStateDto();
        }
    }

    /// <summary>Clamp a client-sent layout to known values so stored state is always usable.</summary>
    internal static SavedViewStateDto Sanitize(SavedViewStateDto s) => new()
    {
        Q = Truncate(s.Q?.Trim() ?? "", MaxQueryLength),
        Sort = string.IsNullOrWhiteSpace(s.Sort) ? "name" : Truncate(s.Sort.Trim(), MaxFieldLength),
        Dir = string.Equals(s.Dir, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc",
        PageSize = PageSizes.Contains(s.PageSize) ? s.PageSize : 100,
        Stacked = s.Stacked,
        HiddenColumns = Fields(s.HiddenColumns),
        ColumnOrder = Fields(s.ColumnOrder),
        Display = s.Display is "table" or "stacks" ? s.Display : null,
        GroupBy = s.GroupBy is "type" or "tag" ? s.GroupBy : null,
    };

    private static List<string> Fields(IEnumerable<string>? fields) =>
        (fields ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f) && f.Length <= MaxFieldLength)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxColumns)
            .ToList();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
