using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Shared.Cards;

namespace OmniCard.Tests.Web.BuyLists;

/// <summary><c>is:buylist</c> on the owned collection: copies matching an entry still wanted.</summary>
public class BuyListSearchTests : IDisposable
{
    private readonly BuyListFixture _f = new();
    private readonly int _bin;

    public BuyListSearchTests()
    {
        _bin = _f.Box("Bin");
        _f.Commits.Commit(_bin,
        [
            BuyListFixture.Scan("Bolt", "bolt-a"),
            BuyListFixture.Scan("Bolt", "bolt-b", foil: true),
            BuyListFixture.Scan("Luffy (Parallel)", "luffy-p"),
            BuyListFixture.Scan("Pikachu - 25/102", "pika"),
            BuyListFixture.Scan("Other", "other"),
        ]);
    }

    public void Dispose() => _f.Dispose();

    private List<string> Search(string query, CardGame? game = CardGame.Mtg)
    {
        using var ctx = _f.Factory.CreateDbContext();
        return CollectionQueryBuilder.BuildFilteredQuery(ctx, query, game, null, null)
            .Select(c => c.GameCardId).ToList().Order().ToList();
    }

    [Fact]
    public void Finds_Every_Owned_Printing()
    {
        _f.Want("Bolt", cardId: "bolt-a");

        Assert.Equal(["bolt-a", "bolt-b"], Search("is:buylist"));
    }

    [Fact]
    public void Finds_Printings_With_Details_In_The_Name()
    {
        _f.Want("Luffy", cardId: "x");
        _f.Want("Pikachu", cardId: "y");

        Assert.Equal(["luffy-p", "pika"], Search("is:buylist"));
    }

    [Fact]
    public void Finds_The_Printing_It_Was_Added_From_Under_Another_Name()
    {
        _f.Want("Emeritus of Conflict // Bolt", cardId: "bolt-a");

        Assert.Equal(["bolt-a"], Search("is:buylist"));
    }

    [Fact]
    public void A_Name_Prefix_Alone_Is_Not_A_Match()
    {
        _f.Want("Bol", cardId: "x");
        _f.Want("Luf", cardId: "y");

        Assert.Empty(Search("is:buylist"));
    }

    [Fact]
    public void Honors_The_Pin_And_The_Finish()
    {
        _f.Want("Bolt", cardId: "bolt-a", pin: true);
        Assert.Equal(["bolt-a"], Search("is:buylist"));

        _f.Want("Luffy (Parallel)", cardId: "luffy-p", foil: true);
        Assert.Equal(["bolt-a"], Search("is:buylist"));
    }

    [Fact]
    public void Skips_Fulfilled_Entries()
    {
        var entry = _f.Want("Bolt", cardId: "bolt-a");
        _f.Service.Update(entry.Id, new Api.Contracts.BuyListEntryUpdateRequest { WantedQuantity = 0 });

        Assert.Empty(Search("is:buylist"));
    }

    [Fact]
    public void Skips_Other_Games()
    {
        _f.Want("Bolt", cardId: "bolt-a", game: "Pokemon");

        Assert.Empty(Search("is:buylist", game: null));
    }

    [Fact]
    public void Works_Negated_And_Under_Or()
    {
        _f.Want("Bolt", cardId: "bolt-a");

        Assert.Equal(["luffy-p", "other", "pika"], Search("-is:buylist"));
        Assert.Equal(["bolt-a", "bolt-b", "other"], Search("is:buylist or name:Other"));
    }

    [Fact]
    public void Combines_With_Other_Fields()
    {
        _f.Want("Bolt", cardId: "bolt-a");

        Assert.Equal(["bolt-b"], Search("is:buylist is:foil"));
    }
}
