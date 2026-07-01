using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Data;

public class ScryfallDbContextTests : IDisposable
{
    private readonly ScryfallDbContext _context;

    public ScryfallDbContextTests()
    {
        var options = new DbContextOptionsBuilder<ScryfallDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new ScryfallDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Fact]
    public void InsertAndQuery_Card_RoundTripsAllFields()
    {
        var cardId = Guid.NewGuid();
        var card = new Card
        {
            Id = cardId,
            OracleId = Guid.NewGuid(),
            MultiverseIds = [457145],
            Name = "Fury Sliver",
            Lang = "en",
            ReleasedAt = "2006-10-06",
            Uri = "https://api.scryfall.com/cards/test",
            ScryfallUri = "https://scryfall.com/card/tsp/157",
            Layout = "normal",
            HighresImage = true,
            ImageStatus = "highres_scan",
            ManaCost = "{5}{R}",
            Cmc = 6.0,
            TypeLine = "Creature \u2014 Sliver",
            OracleText = "All Sliver creatures have double strike.",
            Power = "3",
            Toughness = "3",
            Colors = ["R"],
            ColorIdentity = ["R"],
            Keywords = [],
            Games = ["paper", "mtgo"],
            Finishes = ["nonfoil", "foil"],
            SetId = Guid.NewGuid(),
            SetCode = "tsp",
            SetName = "Time Spiral",
            SetType = "expansion",
            SetUri = "https://api.scryfall.com/sets/test",
            SetSearchUri = "https://api.scryfall.com/cards/search?q=set:tsp",
            ScryfallSetUri = "https://scryfall.com/sets/tsp",
            RulingsUri = "https://api.scryfall.com/cards/test/rulings",
            PrintsSearchUri = "https://api.scryfall.com/cards/search?q=test",
            CollectorNumber = "157",
            Rarity = "uncommon",
            BorderColor = "black",
            Frame = "2003",
            EdhrecRank = 5765,
            ImageUris = new ImageUris
            {
                Small = "https://cards.scryfall.io/small/fury.jpg",
                Normal = "https://cards.scryfall.io/normal/fury.jpg",
                ArtCrop = "https://cards.scryfall.io/art_crop/fury.jpg"
            },
            Prices = new Prices { Usd = "0.35", UsdFoil = "1.25" },
            Legalities = new Dictionary<string, string>
            {
                ["standard"] = "not_legal",
                ["modern"] = "legal",
                ["commander"] = "legal"
            },
            RelatedUris = new Dictionary<string, string>
            {
                ["edhrec"] = "https://edhrec.com/route/?cc=Fury+Sliver"
            },
            PurchaseUris = new Dictionary<string, string>
            {
                ["tcgplayer"] = "https://tcgplayer.com/fury-sliver"
            }
        };

        _context.Cards.Add(card);
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var loaded = _context.Cards.First(c => c.Id == cardId);

        Assert.Equal("Fury Sliver", loaded.Name);
        Assert.Equal("{5}{R}", loaded.ManaCost);
        Assert.Equal(6.0, loaded.Cmc);
        Assert.Equal("3", loaded.Power);
        Assert.Equal("tsp", loaded.SetCode);
        Assert.Equal(5765, loaded.EdhrecRank);
        Assert.NotNull(loaded.MultiverseIds);
        Assert.Contains(457145, loaded.MultiverseIds);
        Assert.Equal(["R"], loaded.Colors);
        Assert.NotNull(loaded.ImageUris);
        Assert.Equal("https://cards.scryfall.io/normal/fury.jpg", loaded.ImageUris.Normal);
        Assert.Equal("https://cards.scryfall.io/art_crop/fury.jpg", loaded.ImageUris.ArtCrop);
        Assert.NotNull(loaded.Prices);
        Assert.Equal("0.35", loaded.Prices.Usd);
        Assert.Equal("1.25", loaded.Prices.UsdFoil);
        Assert.Equal("legal", loaded.Legalities["commander"]);
        Assert.Equal("https://edhrec.com/route/?cc=Fury+Sliver", loaded.RelatedUris!["edhrec"]);
        Assert.Equal("https://tcgplayer.com/fury-sliver", loaded.PurchaseUris!["tcgplayer"]);
    }

    [Fact]
    public void InsertAndQuery_RelatedCards_CascadeFromCard()
    {
        var cardId = Guid.NewGuid();
        var card = new Card
        {
            Id = cardId,
            OracleId = Guid.NewGuid(),
            Name = "Brood Monitor",
            Lang = "en",
            ReleasedAt = "2015-10-02",
            Uri = "https://api.scryfall.com/cards/test",
            ScryfallUri = "https://scryfall.com/card/bfz/164",
            Layout = "normal",
            ImageStatus = "highres_scan",
            TypeLine = "Creature",
            ColorIdentity = [],
            Keywords = [],
            Games = ["paper"],
            Finishes = ["nonfoil"],
            SetId = Guid.NewGuid(),
            SetCode = "bfz",
            SetName = "Battle for Zendikar",
            SetType = "expansion",
            SetUri = "https://api.scryfall.com/sets/test",
            SetSearchUri = "https://api.scryfall.com/cards/search?q=set:bfz",
            ScryfallSetUri = "https://scryfall.com/sets/bfz",
            RulingsUri = "https://api.scryfall.com/cards/test/rulings",
            PrintsSearchUri = "https://api.scryfall.com/cards/search?q=test",
            CollectorNumber = "164",
            Rarity = "uncommon",
            BorderColor = "black",
            Frame = "2015",
            Legalities = new Dictionary<string, string> { ["modern"] = "legal" },
            RelatedCards =
            [
                new RelatedCard
                {
                    CardId = cardId,
                    ScryfallId = Guid.NewGuid(),
                    Component = "token",
                    Name = "Eldrazi Scion",
                    TypeLine = "Token Creature",
                    Uri = "https://api.scryfall.com/cards/token"
                }
            ]
        };

        _context.Cards.Add(card);
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var loaded = _context.Cards.Include(c => c.RelatedCards).First(c => c.Id == cardId);

        Assert.Single(loaded.RelatedCards);
        Assert.Equal("token", loaded.RelatedCards[0].Component);
        Assert.Equal("Eldrazi Scion", loaded.RelatedCards[0].Name);
    }

    [Fact]
    public void NonUniqueIndex_AllowsDuplicateSetCollectorLang()
    {
        // Unique constraint removed in default_cards migration — multiple printings
        // (e.g. foil vs non-foil) may share the same SetCode/CollectorNumber/Lang.
        var card1 = CreateMinimalCard(setCode: "tsp", collectorNumber: "157", lang: "en");
        var card2 = CreateMinimalCard(setCode: "tsp", collectorNumber: "157", lang: "en");

        _context.Cards.Add(card1);
        _context.SaveChanges();
        _context.Cards.Add(card2);

        // Should NOT throw — duplicates are now permitted
        _context.SaveChanges();
        Assert.Equal(2, _context.Cards.Count(c => c.SetCode == "tsp" && c.CollectorNumber == "157" && c.Lang == "en"));
    }

    private static Card CreateMinimalCard(string setCode = "tst", string collectorNumber = "1", string lang = "en")
    {
        return new Card
        {
            Id = Guid.NewGuid(),
            OracleId = Guid.NewGuid(),
            Name = "Test Card",
            Lang = lang,
            ReleasedAt = "2024-01-01",
            Uri = "https://api.scryfall.com/cards/test",
            ScryfallUri = "https://scryfall.com/card/test",
            Layout = "normal",
            ImageStatus = "highres_scan",
            TypeLine = "Creature",
            ColorIdentity = [],
            Keywords = [],
            Games = ["paper"],
            Finishes = ["nonfoil"],
            SetId = Guid.NewGuid(),
            SetCode = setCode,
            SetName = "Test Set",
            SetType = "expansion",
            SetUri = "https://api.scryfall.com/sets/test",
            SetSearchUri = "https://api.scryfall.com/cards/search?q=test",
            ScryfallSetUri = "https://scryfall.com/sets/test",
            RulingsUri = "https://api.scryfall.com/cards/test/rulings",
            PrintsSearchUri = "https://api.scryfall.com/cards/search?q=test",
            CollectorNumber = collectorNumber,
            Rarity = "common",
            BorderColor = "black",
            Frame = "2015",
            Legalities = []
        };
    }
}
