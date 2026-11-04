using OmniCard.Api.Contracts;
using OmniCard.Shared.BuyList;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Storage;
using OmniCard.Web.Services;
using OmniCard.Web.Services.BuyList;

namespace OmniCard.Tests.Web.BuyLists;

/// <summary>The per-game Buy Lists: matching, allocating wanted copies across a commit, the count-down,
/// the set-aside location, and what customers get to see.</summary>
public class BuyListServiceTests : IDisposable
{
    private readonly BuyListFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static BuyListCandidate Card(string name, string id = "printing-1", int quantity = 1, bool foil = false,
        CardGame game = CardGame.Mtg) => new(game, name, id, foil, quantity);

    private static BuyListEntry Entry(string name, string id = "printing-1", bool pin = false, bool? foil = null) => new()
    {
        Name = name, MatchName = BuyListMatcher.BaseName(name), GameCardId = id, PinPrinting = pin, Foil = foil,
    };

    [Theory]
    [InlineData("Lightning Bolt", "Lightning Bolt")]
    [InlineData("Monkey.D.Luffy (Parallel)", "Monkey.D.Luffy")]
    [InlineData("Monkey.D.Luffy (Parallel) (Alternate Art)", "Monkey.D.Luffy")]
    [InlineData("Charizard ex - 199/165", "Charizard ex")]
    [InlineData("Charizard ex - 199/165 (Full Art)", "Charizard ex")]
    [InlineData("Fire // Ice", "Fire // Ice")]
    [InlineData("Jace - the Mind Sculptor", "Jace - the Mind Sculptor")]
    [InlineData("  Spaced Name  ", "Spaced Name")]
    public void BaseName_Drops_Only_Printing_Details(string name, string expected) =>
        Assert.Equal(expected, BuyListMatcher.BaseName(name));

    [Fact]
    public void Matches_Any_Printing_By_Default()
    {
        var entry = Entry("Monkey.D.Luffy (Parallel)");

        Assert.True(BuyListMatcher.Matches(entry, "Monkey.D.Luffy", "other", false));
        Assert.True(BuyListMatcher.Matches(entry, "MONKEY.D.LUFFY (Alternate Art)", "other", true));
        Assert.False(BuyListMatcher.Matches(entry, "Roronoa Zoro", "other", false));
    }

    [Fact]
    public void Matches_The_Printing_It_Was_Added_From_Under_Another_Name() =>
        Assert.True(BuyListMatcher.Matches(Entry("Emeritus of Conflict // Lightning Bolt"), "Lightning Bolt", "printing-1", false));

    [Fact]
    public void Matches_Only_The_Pinned_Printing_And_Finish()
    {
        var pinned = Entry("Bolt", pin: true);
        var foilOnly = Entry("Bolt", foil: true);
        var nonFoilOnly = Entry("Bolt", foil: false);

        Assert.True(BuyListMatcher.Matches(pinned, "Bolt", "printing-1", false));
        Assert.False(BuyListMatcher.Matches(pinned, "Bolt", "printing-2", false));
        Assert.True(BuyListMatcher.Matches(foilOnly, "Bolt", "printing-2", true));
        Assert.False(BuyListMatcher.Matches(foilOnly, "Bolt", "printing-2", false));
        Assert.False(BuyListMatcher.Matches(nonFoilOnly, "Bolt", "printing-2", true));
    }

    [Fact]
    public void Add_Raises_The_Quantity_Of_A_Card_Already_On_The_List()
    {
        var first = _f.Want("Bolt", quantity: 2);
        var again = _f.Want("Bolt", cardId: "printing-2", quantity: 3, priority: "Grail");

        Assert.False(first.Merged);
        Assert.True(again.Merged);
        Assert.Equal(first.Id, again.Id);
        var entry = Assert.Single(_f.Service.List(CardGame.Mtg, false, null).Items);
        Assert.Equal((5, "Grail"), (entry.WantedQuantity, entry.Priority));
    }

    [Fact]
    public void Add_Keeps_A_Pinned_Printing_Apart_From_Any_Printing()
    {
        _f.Want("Bolt");
        _f.Want("Bolt", pin: true);
        _f.Want("Bolt", foil: true);

        Assert.Equal(3, _f.Service.List(CardGame.Mtg, false, null).Items.Count);
    }

    [Fact]
    public void Add_Brings_Back_A_Fulfilled_Card()
    {
        var entry = _f.Want("Bolt");
        _f.Service.Consume(_f.Service.Allocate([Card("Bolt")]));

        _f.Want("Bolt", quantity: 2);

        Assert.Equal(entry.Id, Assert.Single(_f.Service.List(CardGame.Mtg, false, null).Items).Id);
        Assert.Equal(0, _f.Service.List(CardGame.Mtg, false, null).FulfilledCount);
    }

    [Theory]
    [InlineData("", "id")]
    [InlineData("Bolt", "")]
    public void Add_Needs_A_Picked_Card(string name, string id) =>
        Assert.Throws<BuyListException>(() => _f.Want(name, cardId: id));

    [Fact]
    public void Add_Refuses_An_Unknown_Game() =>
        Assert.Throws<BuyListException>(() => _f.Want("Bolt", game: "Chess"));

    [Fact]
    public void List_Is_Per_Game_And_Highest_Priority_First()
    {
        _f.Want("Apple", priority: "Normal");
        _f.Want("Zebra", priority: "Grail");
        _f.Want("Mango", priority: "High");
        _f.Want("Charizard", game: "Pokemon");

        var items = _f.Service.List(CardGame.Mtg, false, null).Items;

        Assert.Equal(["Zebra", "Mango", "Apple"], items.Select(i => i.Name));
        Assert.Equal(4, _f.Service.List(null, false, null).Items.Count);
    }

    [Fact]
    public void List_Counts_Owned_Copies()
    {
        var bin = _f.Box("Bin");
        _f.Commits.Commit(bin, [BuyListFixture.Scan("Bolt", quantity: 3), BuyListFixture.Scan("Bolt", "printing-2"), BuyListFixture.Scan("Other", "x")]);
        _f.Want("Bolt", quantity: 2);

        Assert.Equal(4, Assert.Single(_f.Service.List(CardGame.Mtg, false, null).Items).OwnedCopies);
    }

    [Fact]
    public void List_Owned_Copies_Stay_Within_The_Readable_Sites()
    {
        _f.Commits.Commit(_f.Box("Bin"), [BuyListFixture.Scan("Bolt")]);
        _f.Want("Bolt");

        Assert.Equal(0, Assert.Single(_f.Service.List(CardGame.Mtg, false, [999]).Items).OwnedCopies);
    }

    [Fact]
    public void Allocate_Claims_Only_The_Copies_Still_Wanted()
    {
        _f.Want("Bolt", quantity: 2);

        var result = _f.Service.Allocate([Card("Bolt", quantity: 3), Card("Bolt"), Card("Other", "other")]);

        Assert.Equal([2, 0, 0], result.Select(a => a.Copies));
        Assert.NotNull(result[0].Hit);
        Assert.NotNull(result[1].Hit); // nothing left to claim, but it's still a wanted card
        Assert.Null(result[2].Hit);
    }

    [Fact]
    public void Allocate_Takes_From_The_Highest_Priority_Entry_First()
    {
        var any = _f.Want("Bolt", quantity: 1);
        var pinned = _f.Want("Bolt", pin: true, quantity: 1, priority: "High");

        var result = Assert.Single(_f.Service.Allocate([Card("Bolt", quantity: 2)]));

        Assert.Equal([pinned.Id, any.Id], result.Takes.Select(t => t.EntryId));
        Assert.Equal((pinned.Id, "High", 2), (result.Hit!.EntryId, result.Hit.Priority, result.Hit.WantedQuantity));
    }

    [Fact]
    public void Allocate_A_Left_Out_Card_Is_Flagged_But_Claims_Nothing()
    {
        _f.Want("Bolt");

        var result = Assert.Single(_f.Service.Allocate([Card("Bolt", quantity: 0)]));

        Assert.NotNull(result.Hit);
        Assert.Equal(0, result.Copies);
    }

    [Fact]
    public void Allocate_Does_Not_Cross_Games()
    {
        _f.Want("Bolt", game: "Pokemon");

        Assert.Null(Assert.Single(_f.Service.Allocate([Card("Bolt")])).Hit);
    }

    [Fact]
    public void Consume_Counts_Down_And_A_Fulfilled_Entry_Stops_Flagging()
    {
        _f.Want("Bolt", quantity: 2);

        _f.Service.Consume(_f.Service.Allocate([Card("Bolt", quantity: 2)]));

        var page = _f.Service.List(CardGame.Mtg, false, null);
        Assert.Empty(page.Items);
        Assert.Equal(1, page.FulfilledCount);
        var fulfilled = Assert.Single(_f.Service.List(CardGame.Mtg, true, null).Items);
        Assert.Equal((0, 2), (fulfilled.WantedQuantity, fulfilled.AcquiredQuantity));
        Assert.Empty(_f.Service.Evaluate(CardGame.Mtg, [new ScanTagRuleItem { Key = "s1", Name = "Bolt", GameCardId = "printing-1" }]));
    }

    [Fact]
    public void Consume_Never_Goes_Below_Zero()
    {
        _f.Want("Bolt");
        var first = _f.Service.Allocate([Card("Bolt")]);
        var second = _f.Service.Allocate([Card("Bolt")]);

        _f.Service.Consume(first);
        _f.Service.Consume(second);

        var entry = Assert.Single(_f.Service.List(CardGame.Mtg, true, null).Items);
        Assert.Equal((0, 1), (entry.WantedQuantity, entry.AcquiredQuantity));
    }

    [Fact]
    public void Evaluate_Echoes_The_Key_And_Carries_The_Staff_Note()
    {
        _f.Want("Bolt", quantity: 4, priority: "High", note: "Pay up for NM");
        _f.Settings.Value.SetAsideLocationIds[CardGame.Mtg] = _f.Box("For the owner");

        var hit = Assert.Single(_f.Service.Evaluate(CardGame.Mtg,
        [
            new ScanTagRuleItem { Key = "s1", Name = "Other", GameCardId = "x" },
            new ScanTagRuleItem { Key = "s2", Name = "Bolt", GameCardId = "y" },
        ]));

        Assert.Equal("s2", hit.Key);
        Assert.Equal(("High", 4, "Pay up for NM", "For the owner"),
            (hit.Hit.Priority, hit.Hit.WantedQuantity, hit.Hit.Note, hit.Hit.SetAsideLocationName));
    }

    [Fact]
    public void Update_To_Zero_Fulfills_And_Raising_It_Brings_It_Back()
    {
        var entry = _f.Want("Bolt");

        _f.Service.Update(entry.Id, new BuyListEntryUpdateRequest { WantedQuantity = 0 });
        Assert.Empty(_f.Service.List(CardGame.Mtg, false, null).Items);

        _f.Service.Update(entry.Id, new BuyListEntryUpdateRequest { WantedQuantity = 2, PinPrinting = true, Foil = true, Priority = "Grail", Note = " n " });
        var updated = Assert.Single(_f.Service.List(CardGame.Mtg, false, null).Items);
        Assert.Equal((2, true, (bool?)true, "Grail", "n"), (updated.WantedQuantity, updated.PinPrinting, updated.Foil, updated.Priority, updated.Note));
    }

    [Fact]
    public void Update_And_Delete_Report_A_Missing_Entry()
    {
        Assert.Null(_f.Service.Update(404, new BuyListEntryUpdateRequest()));
        Assert.False(_f.Service.Delete(404));
    }

    [Fact]
    public void SetAside_Refuses_A_Deck_Box_For_Another_Game()
    {
        var deck = _f.Box("Pikachu deck", ContainerType.DeckBox, CardGame.Pokemon);

        Assert.Throws<BuyListException>(() => _f.Service.SetSetAside(CardGame.Mtg, deck));
        Assert.Throws<BuyListException>(() => _f.Service.SetSetAside(CardGame.Mtg, 404));
    }

    [Fact]
    public void SetAside_Ignores_A_Location_That_Was_Deleted()
    {
        _f.Settings.Value.SetAsideLocationIds[CardGame.Mtg] = 404;

        Assert.Null(_f.Service.SetAsideLocation(CardGame.Mtg));
    }

    [Fact]
    public void SetAside_Can_Be_Cleared()
    {
        _f.Service.SetSetAside(CardGame.Mtg, _f.Box("For the owner"));
        Assert.NotNull(_f.Service.SetAsideLocation(CardGame.Mtg));

        _f.Service.SetSetAside(CardGame.Mtg, null);

        Assert.Null(_f.Service.SetAsideLocation(CardGame.Mtg));
    }

    [Fact]
    public void ForKiosk_Lists_Only_Wanted_Cards_Most_Wanted_First()
    {
        _f.Want("Apple");
        _f.Want("Zebra", priority: "Grail");
        var bought = _f.Want("Gone");
        _f.Service.Update(bought.Id, new BuyListEntryUpdateRequest { WantedQuantity = 0 });

        Assert.Equal(["Zebra", "Apple"], _f.Service.ForKiosk().Select(e => e.Name));
    }

    [Fact]
    public void ForKiosk_Names_The_Printing_Only_When_It_Is_Pinned()
    {
        _f.Want("Monkey.D.Luffy (Parallel)");
        _f.Want("Roronoa Zoro (Parallel)", cardId: "printing-2", pin: true);

        var entries = _f.Service.ForKiosk();

        Assert.Equal(("Monkey.D.Luffy", null), (entries[0].Name, entries[0].SetName));
        Assert.Equal(("Roronoa Zoro (Parallel)", "Test Set"), (entries[1].Name, entries[1].SetName));
    }

    [Fact]
    public void CommitWithBuyList_Sets_Aside_Wanted_Copies_And_Splits_A_Stack()
    {
        var bin = _f.Box("Bin");
        var setAside = _f.Box("For the owner");
        _f.Service.SetSetAside(CardGame.Mtg, setAside);
        _f.Want("Bolt", quantity: 1);

        var outcome = _f.Commits.CommitWithBuyList(bin, [BuyListFixture.Scan("Bolt", quantity: 3), BuyListFixture.Scan("Other", "x")]);

        using var db = _f.Factory.CreateDbContext();
        var lots = db.Lots.ToList();
        Assert.Equal(3, outcome.LotIds.Count);
        Assert.Equal(1, lots.Single(l => l.LocationId == setAside).Quantity);
        Assert.Equal(3, lots.Where(l => l.LocationId == bin).Sum(l => l.Quantity));
        Assert.Equal((1, 1, 0), (outcome.BuyList!.MatchedCopies, outcome.BuyList.SetAsideCopies, outcome.BuyList.NotSetAside));
        Assert.Equal(["For the owner"], outcome.BuyList.SetAsideLocations);
        Assert.Equal(0, db.BuyListEntries.Single().WantedQuantity);
    }

    [Fact]
    public void CommitWithBuyList_Leaves_Copies_In_Place_Without_A_Set_Aside_Location()
    {
        var bin = _f.Box("Bin");
        _f.Want("Bolt", quantity: 2);

        var outcome = _f.Commits.CommitWithBuyList(bin, [BuyListFixture.Scan("Bolt")]);

        using var db = _f.Factory.CreateDbContext();
        Assert.Equal(bin, db.Lots.Single().LocationId);
        Assert.Equal((1, 0), (outcome.BuyList!.MatchedCopies, outcome.BuyList.SetAsideCopies));
        Assert.Equal(1, db.BuyListEntries.Single().WantedQuantity);
    }

    [Fact]
    public void CommitWithBuyList_Stays_Put_When_The_User_Cannot_Write_To_The_Set_Aside_Site()
    {
        var bin = _f.Box("Bin");
        var setAside = _f.Box("For the owner");
        _f.Service.SetSetAside(CardGame.Mtg, setAside);
        _f.Want("Bolt");

        var outcome = _f.Commits.CommitWithBuyList(bin, [BuyListFixture.Scan("Bolt")], canWriteLocation: id => id != setAside);

        using var db = _f.Factory.CreateDbContext();
        Assert.Equal(bin, db.Lots.Single().LocationId);
        Assert.Equal((0, 1), (outcome.BuyList!.SetAsideCopies, outcome.BuyList.NotSetAside));
        Assert.Equal(0, db.BuyListEntries.Single().WantedQuantity);
    }

    [Fact]
    public void CommitWithBuyList_Scanning_Into_The_Set_Aside_Location_Is_Not_A_Move()
    {
        var setAside = _f.Box("For the owner");
        _f.Service.SetSetAside(CardGame.Mtg, setAside);
        _f.Want("Bolt");

        var outcome = _f.Commits.CommitWithBuyList(setAside, [BuyListFixture.Scan("Bolt")]);

        Assert.Equal(0, outcome.BuyList!.SetAsideCopies);
        Assert.Equal(1, outcome.BuyList.MatchedCopies);
    }

    [Fact]
    public void CommitWithBuyList_Reports_Nothing_When_No_Card_Is_Wanted()
    {
        _f.Want("Bolt");

        Assert.Null(_f.Commits.CommitWithBuyList(_f.Box("Bin"), [BuyListFixture.Scan("Other", "x")]).BuyList);
    }

    [Fact]
    public void CommitWithBuyList_Refuses_An_Unknown_Game_Before_Writing()
    {
        var bin = _f.Box("Bin");

        Assert.Throws<UnknownScanGameException>(() =>
            _f.Commits.CommitWithBuyList(bin, [BuyListFixture.Scan("Bolt"), BuyListFixture.Scan("Bolt") with { Game = "Chess" }]));

        using var db = _f.Factory.CreateDbContext();
        Assert.Empty(db.Lots);
    }

    [Fact]
    public void Commit_Without_The_Buy_List_Leaves_It_Alone()
    {
        var bin = _f.Box("Bin");
        _f.Service.SetSetAside(CardGame.Mtg, _f.Box("For the owner"));
        _f.Want("Bolt");

        _f.Commits.Commit(bin, [BuyListFixture.Scan("Bolt")]);

        using var db = _f.Factory.CreateDbContext();
        Assert.Equal(bin, db.Lots.Single().LocationId);
        Assert.Equal(1, db.BuyListEntries.Single().WantedQuantity);
    }
}
