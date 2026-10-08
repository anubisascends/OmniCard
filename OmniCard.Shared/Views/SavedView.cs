namespace OmniCard.Shared.Views;

/// <summary>Which card list a <see cref="SavedView"/> belongs to.</summary>
public enum SavedViewPage
{
    /// <summary>The Collection page (every location the user can see).</summary>
    Collection = 0,

    /// <summary>One location's detail page (<see cref="SavedView.ContainerId"/>).</summary>
    Location = 1,

    /// <summary>Every location's detail page. Shared (admin-published) views only.</summary>
    AllLocations = 2,
}

/// <summary>
/// A named, saved look of the Collection page or a Location page: the search query, sort, page size,
/// stacking, column layout and (Location only) table/stacks display + group-by. The layout itself is
/// opaque here (<see cref="StateJson"/>); the web layer owns its shape.
///
/// <para><b>Scope.</b> A view is either personal (<see cref="UserId"/> set) or shared — published by an
/// administrator for everyone (<see cref="UserId"/> null), read-only to other users. It belongs to one
/// page (<see cref="Page"/> + <see cref="ContainerId"/>) and to one game key: a specific game, the
/// "All Games" selection (<see cref="AllGames"/>), or any game (<see cref="AnyGame"/>).</para>
///
/// <para>Personal views go with their user, and location views with their location (cascade).</para>
/// </summary>
public class SavedView
{
    /// <summary><see cref="GameKey"/> for a view offered whatever game is selected.</summary>
    public const string AnyGame = "";

    /// <summary><see cref="GameKey"/> for the "All Games" selection (no game filter).</summary>
    public const string AllGames = "all";

    public const int MaxNameLength = 100;

    public int Id { get; set; }

    /// <summary>Owner; null = a shared view every user can pick.</summary>
    public int? UserId { get; set; }

    public SavedViewPage Page { get; set; }

    /// <summary>The location, for <see cref="SavedViewPage.Location"/> views; otherwise null.</summary>
    public int? ContainerId { get; set; }

    /// <summary>A <see cref="Cards.CardGame"/> name, <see cref="AllGames"/> or <see cref="AnyGame"/>.</summary>
    public string GameKey { get; set; } = AnyGame;

    public string Name { get; set; } = "";

    /// <summary>The saved layout, serialized by the web layer.</summary>
    public string StateJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public bool IsShared => UserId is null;
}

/// <summary>
/// "Open this view by default" for one page + game key, either for one user (<see cref="UserId"/>) or
/// for everyone (<see cref="UserId"/> null — set by an administrator, always pointing at a shared view).
/// A user's own default wins over everyone's. Rows go with their view (cascade); rows for a deleted
/// user or location are removed by the web layer.
/// </summary>
public class SavedViewDefault
{
    public int Id { get; set; }

    /// <summary>The user this default is for; null = everyone without a default of their own.</summary>
    public int? UserId { get; set; }

    public SavedViewPage Page { get; set; }
    public int? ContainerId { get; set; }

    /// <summary>The game key this default applies to — always the view's own <see cref="SavedView.GameKey"/>.</summary>
    public string GameKey { get; set; } = SavedView.AnyGame;

    public int SavedViewId { get; set; }
}
