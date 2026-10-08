using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Collection.Inventory;
using OmniCard.Data;
using OmniCard.Shared.Settings;
using OmniCard.Shared.Storage;
using OmniCard.Shared.Views;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>
/// Saved views of the Collection / Location pages: who sees and edits which view (personal vs
/// admin-shared), game and location scoping, the default-resolution order, copying a location's view
/// to other locations, and cleanup when a view, location or user goes away.
/// </summary>
public class SavedViewServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly MockFactory _factory;
    private readonly SavedViewService _views;
    private readonly int _alice;
    private readonly int _bob;
    private readonly int _admin;
    private readonly int _binderA;
    private readonly int _binderB;
    private readonly int _binderC;

    public SavedViewServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();
        _factory = new MockFactory(_opts);
        _views = new SavedViewService(_factory, TimeProvider.System);
        _alice = AddUser("alice");
        _bob = AddUser("bob");
        _admin = AddUser("boss", isAdmin: true);
        _binderA = AddLocation("Binder A");
        _binderB = AddLocation("Binder B");
        _binderC = AddLocation("Binder C");
    }

    public void Dispose() => _conn.Dispose();

    // --- helpers -------------------------------------------------------------------------------

    private OmniCardDbContext Db() => new(_opts);

    private int AddUser(string name, bool isAdmin = false)
    {
        using var db = Db();
        var u = new User { Username = name, PasswordHash = "x", IsAdmin = isAdmin };
        db.Users.Add(u);
        db.SaveChanges();
        return u.Id;
    }

    private int AddLocation(string name)
    {
        using var db = Db();
        var c = new StorageContainer { Name = name, ContainerType = ContainerType.Binder };
        db.StorageContainers.Add(c);
        db.SaveChanges();
        return c.Id;
    }

    private SavedViewDto Create(int userId, string name, string page = "Collection", int? containerId = null,
        string? game = null, bool shared = false, string q = "", bool isAdmin = false) =>
        _views.Create(userId, isAdmin || shared, new CreateSavedViewRequest
        {
            Name = name, Page = page, ContainerId = containerId, Game = game, Shared = shared,
            State = new SavedViewStateDto { Q = q },
        });

    private SavedViewListDto Collection(int userId, string game = "Mtg") =>
        _views.List(userId, userId == _admin, SavedViewPage.Collection, null, game);

    private SavedViewListDto Location(int userId, int containerId, string game = "Mtg") =>
        _views.List(userId, userId == _admin, SavedViewPage.Location, containerId, game);

    private static string[] Names(SavedViewListDto list) => list.Views.Select(v => v.Name).ToArray();

    private static SavedViewErrorKind Fails(Action action) =>
        Assert.Throws<SavedViewException>(action).Kind;

    // --- visibility + editing ------------------------------------------------------------------

    [Fact]
    public void PersonalViews_AreOnlyVisibleToTheirOwner()
    {
        Create(_alice, "Alice's");
        Create(_bob, "Bob's");

        Assert.Equal(["Alice's"], Names(Collection(_alice)));
        Assert.Equal(["Bob's"], Names(Collection(_bob)));
        // Admins don't see other users' personal views either.
        Assert.Empty(Collection(_admin).Views);
    }

    [Fact]
    public void SharedViews_AreVisibleToEveryone_ButOnlyAdminsEditThem()
    {
        var shared = Create(_admin, "House view", shared: true);

        var bobsView = Assert.Single(Collection(_bob).Views);
        Assert.True(bobsView.Shared);
        Assert.False(bobsView.CanEdit);
        Assert.True(Assert.Single(Collection(_admin).Views).CanEdit);

        Assert.Equal(SavedViewErrorKind.Forbidden, Fails(() =>
            _views.Update(shared.Id, _bob, false, new UpdateSavedViewRequest { Name = "Mine now" })));
        Assert.Equal(SavedViewErrorKind.Forbidden, Fails(() => _views.Delete(shared.Id, _bob, false)));
        Assert.Equal(SavedViewErrorKind.Forbidden, Fails(() =>
            _views.Create(_bob, false, new CreateSavedViewRequest { Name = "x", Shared = true })));
    }

    [Fact]
    public void OtherUsersPersonalViews_CantBeReadOrChanged()
    {
        var alices = Create(_alice, "Alice's");

        Assert.Equal(SavedViewErrorKind.NotFound, Fails(() => _views.Get(alices.Id, _bob, false)));
        Assert.Equal(SavedViewErrorKind.NotFound, Fails(() => _views.Delete(alices.Id, _admin, true)));
    }

    [Fact]
    public void Views_AreScopedToTheirGame_AnyGameViewsShowForEveryGame()
    {
        Create(_alice, "MTG only", game: "Mtg");
        Create(_alice, "Pokémon only", game: "pokemon");
        Create(_alice, "All Games", game: SavedView.AllGames);
        Create(_alice, "Any game");

        Assert.Equal(["Any game", "MTG only"], Names(Collection(_alice, "Mtg")));
        Assert.Equal(["Any game", "Pokémon only"], Names(Collection(_alice, "Pokemon")));
        Assert.Equal(["All Games", "Any game"], Names(Collection(_alice, SavedView.AllGames)));
        Assert.Equal("Pokemon", Collection(_alice, "Pokemon").Views.Single(v => v.Name == "Pokémon only").Game);
        Assert.Null(Collection(_alice).Views.Single(v => v.Name == "Any game").Game);
    }

    [Fact]
    public void LocationViews_StayOnTheirLocation_AllLocationsSharedViewsShowOnEveryLocation()
    {
        Create(_alice, "Collection view");
        Create(_alice, "A view", "Location", _binderA);
        Create(_admin, "Everywhere", "AllLocations", shared: true);

        Assert.Equal(["A view", "Everywhere"], Names(Location(_alice, _binderA)));
        Assert.Equal(["Everywhere"], Names(Location(_alice, _binderB)));
        Assert.Equal(["Collection view"], Names(Collection(_alice)));
    }

    [Fact]
    public void AllLocationsViews_MustBeShared()
    {
        Assert.Equal(SavedViewErrorKind.BadRequest, Fails(() =>
            _views.Create(_admin, true, new CreateSavedViewRequest { Name = "x", Page = "AllLocations" })));
    }

    [Fact]
    public void Names_AreUniquePerOwnerPageAndGame()
    {
        Create(_alice, "By price", game: "Mtg");

        Assert.Equal(SavedViewErrorKind.Conflict, Fails(() => Create(_alice, "BY PRICE", game: "Mtg")));
        Create(_alice, "By price", game: "Pokemon");
        Create(_alice, "By price", "Location", _binderA, game: "Mtg");
        Create(_bob, "By price", game: "Mtg");
    }

    [Fact]
    public void Update_RenamesAndSavesTheLayout()
    {
        var view = Create(_alice, "Draft", q: "t:goblin");

        var updated = _views.Update(view.Id, _alice, false, new UpdateSavedViewRequest
        {
            Name = "  Goblins  ",
            State = new SavedViewStateDto { Q = "t:goblin r:rare", Sort = "marketprice", Dir = "desc", PageSize = 50 },
        });

        Assert.Equal("Goblins", updated.Name);
        Assert.Equal("t:goblin r:rare", updated.State.Q);
        Assert.Equal("desc", updated.State.Dir);
        Assert.Equal(50, updated.State.PageSize);
    }

    [Fact]
    public void State_IsClampedToKnownValues()
    {
        var s = SavedViewService.Sanitize(new SavedViewStateDto
        {
            Q = "  q  ", Sort = "", Dir = "sideways", PageSize = 7, Display = "grid", GroupBy = "tag",
            HiddenColumns = ["rarity", "rarity", "", "language"],
        });

        Assert.Equal("q", s.Q);
        Assert.Equal("name", s.Sort);
        Assert.Equal("asc", s.Dir);
        Assert.Equal(100, s.PageSize);
        Assert.Null(s.Display);
        Assert.Equal("tag", s.GroupBy);
        Assert.Equal(["rarity", "language"], s.HiddenColumns);
    }

    // --- defaults ------------------------------------------------------------------------------

    [Fact]
    public void Default_IsTheBuiltInLayoutWhenNothingIsSet()
    {
        Create(_alice, "Unused");
        Assert.Null(Collection(_alice).DefaultViewId);
    }

    [Fact]
    public void Default_ResolvesMineThenEveryones_GameSpecificBeforeAnyGame()
    {
        var everyoneAny = Create(_admin, "Everyone any", shared: true);
        var everyoneMtg = Create(_admin, "Everyone MTG", game: "Mtg", shared: true);
        var mineAny = Create(_alice, "Mine any");
        var mineMtg = Create(_alice, "Mine MTG", game: "Mtg");
        _views.SetDefault(everyoneAny.Id, _admin, true, everyone: true, SavedViewPage.Collection, null);

        Assert.Equal(everyoneAny.Id, Collection(_alice).DefaultViewId);

        _views.SetDefault(everyoneMtg.Id, _admin, true, everyone: true, SavedViewPage.Collection, null);
        Assert.Equal(everyoneMtg.Id, Collection(_alice).DefaultViewId);
        // Another game only matches the any-game default.
        Assert.Equal(everyoneAny.Id, Collection(_alice, "Pokemon").DefaultViewId);

        _views.SetDefault(mineAny.Id, _alice, false, everyone: false, SavedViewPage.Collection, null);
        Assert.Equal(mineAny.Id, Collection(_alice).DefaultViewId);

        _views.SetDefault(mineMtg.Id, _alice, false, everyone: false, SavedViewPage.Collection, null);
        var list = Collection(_alice);
        Assert.Equal(mineMtg.Id, list.DefaultViewId);
        Assert.True(list.Views.Single(v => v.Id == mineMtg.Id).IsMyDefault);
        Assert.True(list.Views.Single(v => v.Id == everyoneMtg.Id).IsEveryoneDefault);

        // Bob has no default of his own, so he gets everyone's.
        Assert.Equal(everyoneMtg.Id, Collection(_bob).DefaultViewId);
    }

    [Fact]
    public void SettingAnAnyGameDefault_ReplacesTheDefaultForTheGameOnScreen_ButNotOtherGames()
    {
        var mtg = Create(_alice, "MTG", game: "Mtg");
        var poke = Create(_alice, "Pokémon", game: "Pokemon");
        var any = Create(_alice, "Any");
        _views.SetDefault(mtg.Id, _alice, false, false, SavedViewPage.Collection, null, "Mtg");
        _views.SetDefault(poke.Id, _alice, false, false, SavedViewPage.Collection, null, "Pokemon");

        _views.SetDefault(any.Id, _alice, false, false, SavedViewPage.Collection, null, "Mtg");

        Assert.Equal(any.Id, Collection(_alice, "Mtg").DefaultViewId);
        Assert.Equal(poke.Id, Collection(_alice, "Pokemon").DefaultViewId);
    }

    [Fact]
    public void Default_OnALocation_FallsBackToEveryonesAllLocationsDefault()
    {
        var everywhere = Create(_admin, "Everywhere", "AllLocations", shared: true);
        var binderAShared = Create(_admin, "A house view", "Location", _binderA, shared: true);
        _views.SetDefault(everywhere.Id, _admin, true, everyone: true, SavedViewPage.Location, _binderA);

        Assert.Equal(everywhere.Id, Location(_alice, _binderA).DefaultViewId);
        Assert.Equal(everywhere.Id, Location(_alice, _binderB).DefaultViewId);
        Assert.Null(Collection(_alice).DefaultViewId);

        _views.SetDefault(binderAShared.Id, _admin, true, everyone: true, SavedViewPage.Location, _binderA);
        Assert.Equal(binderAShared.Id, Location(_alice, _binderA).DefaultViewId);
        Assert.Equal(everywhere.Id, Location(_alice, _binderB).DefaultViewId);
    }

    [Fact]
    public void MyDefault_FromAnAllLocationsView_AppliesOnlyToTheLocationIAmOn()
    {
        var everywhere = Create(_admin, "Everywhere", "AllLocations", shared: true);

        _views.SetDefault(everywhere.Id, _alice, false, everyone: false, SavedViewPage.Location, _binderA);

        Assert.Equal(everywhere.Id, Location(_alice, _binderA).DefaultViewId);
        Assert.Null(Location(_alice, _binderB).DefaultViewId);
        Assert.Null(Location(_bob, _binderA).DefaultViewId);
    }

    [Fact]
    public void SetDefault_RejectsViewsNotOfferedOnThePage_AndEveryoneDefaultsNeedAnAdminAndASharedView()
    {
        var aView = Create(_alice, "A view", "Location", _binderA);
        var shared = Create(_admin, "Shared", shared: true);
        var adminsOwn = Create(_admin, "Admin's own");

        Assert.Equal(SavedViewErrorKind.BadRequest, Fails(() =>
            _views.SetDefault(aView.Id, _alice, false, false, SavedViewPage.Location, _binderB)));
        Assert.Equal(SavedViewErrorKind.Forbidden, Fails(() =>
            _views.SetDefault(shared.Id, _alice, false, true, SavedViewPage.Collection, null)));
        Assert.Equal(SavedViewErrorKind.BadRequest, Fails(() =>
            _views.SetDefault(adminsOwn.Id, _admin, true, true, SavedViewPage.Collection, null)));
    }

    [Fact]
    public void SettingANewDefault_ReplacesTheOldOne_AndClearingFallsBack()
    {
        var first = Create(_alice, "First");
        var second = Create(_alice, "Second");

        _views.SetDefault(first.Id, _alice, false, false, SavedViewPage.Collection, null);
        _views.SetDefault(second.Id, _alice, false, false, SavedViewPage.Collection, null);
        Assert.Equal(second.Id, Collection(_alice).DefaultViewId);

        _views.ClearDefault(second.Id, _alice, false, false, SavedViewPage.Collection, null);
        Assert.Null(Collection(_alice).DefaultViewId);
    }

    [Fact]
    public void DeletingTheDefaultView_FallsBackToTheNextDefault()
    {
        var everyone = Create(_admin, "Everyone", shared: true);
        var mine = Create(_alice, "Mine");
        _views.SetDefault(everyone.Id, _admin, true, true, SavedViewPage.Collection, null);
        _views.SetDefault(mine.Id, _alice, false, false, SavedViewPage.Collection, null);

        _views.Delete(mine.Id, _alice, false);

        Assert.Equal(everyone.Id, Collection(_alice).DefaultViewId);
    }

    // --- copy ----------------------------------------------------------------------------------

    [Fact]
    public void Copy_PushesTheViewToOtherLocations_OverwritingSameNamedViews_AndCanSetDefault()
    {
        var source = Create(_alice, "Value", "Location", _binderA, game: "Mtg", q: "usd>5");
        Create(_alice, "value", "Location", _binderB, game: "Mtg", q: "old");

        var copied = _views.Copy(source.Id, _alice, false, [_binderA, _binderB, _binderC, 9999], setDefault: true);

        Assert.Equal(2, copied);
        var onB = Assert.Single(Location(_alice, _binderB).Views);
        Assert.Equal("usd>5", onB.State.Q);
        Assert.Equal("value", onB.Name);
        var onC = Location(_alice, _binderC);
        Assert.Equal("usd>5", Assert.Single(onC.Views).State.Q);
        Assert.Equal(onC.Views[0].Id, onC.DefaultViewId);
        // The source location's own default is untouched.
        Assert.Null(Location(_alice, _binderA).DefaultViewId);
        Assert.Empty(Location(_bob, _binderC).Views);
    }

    [Fact]
    public void Copy_OfASharedView_StaysShared_AndSetsEveryonesDefault()
    {
        var source = Create(_admin, "House", "Location", _binderA, shared: true);

        _views.Copy(source.Id, _admin, true, [_binderB], setDefault: true);

        var bobOnB = Location(_bob, _binderB);
        Assert.True(Assert.Single(bobOnB.Views).Shared);
        Assert.Equal(bobOnB.Views[0].Id, bobOnB.DefaultViewId);
        Assert.Equal(SavedViewErrorKind.Forbidden, Fails(() => _views.Copy(source.Id, _bob, false, [_binderC], false)));
    }

    [Fact]
    public void Copy_OnlyAppliesToLocationViews()
    {
        var collectionView = Create(_alice, "Collection");
        Assert.Equal(SavedViewErrorKind.BadRequest, Fails(() =>
            _views.Copy(collectionView.Id, _alice, false, [_binderA], false)));
    }

    // --- cleanup -------------------------------------------------------------------------------

    [Fact]
    public void DeletingALocation_RemovesItsViewsAndDefaults()
    {
        var everywhere = Create(_admin, "Everywhere", "AllLocations", shared: true);
        Create(_alice, "B view", "Location", _binderB);
        _views.SetDefault(everywhere.Id, _alice, false, false, SavedViewPage.Location, _binderB);

        new StorageContainerService(_factory).Delete(_binderB, moveCardsToBulk: false);
        _views.RemoveLocation(_binderB);

        using var db = Db();
        Assert.Equal(["Everywhere"], db.SavedViews.Select(v => v.Name).ToArray());
        Assert.Empty(db.SavedViewDefaults);
    }

    [Fact]
    public void DeletingAUser_RemovesTheirViewsAndDefaults()
    {
        var shared = Create(_admin, "Shared", shared: true);
        Create(_alice, "Mine");
        _views.SetDefault(shared.Id, _alice, false, false, SavedViewPage.Collection, null);

        using (var db = Db())
        {
            db.Users.Remove(db.Users.Single(u => u.Id == _alice));
            db.SaveChanges();
        }
        _views.RemoveUser(_alice);

        using var check = Db();
        Assert.Equal(["Shared"], check.SavedViews.Select(v => v.Name).ToArray());
        Assert.Empty(check.SavedViewDefaults);
    }

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
