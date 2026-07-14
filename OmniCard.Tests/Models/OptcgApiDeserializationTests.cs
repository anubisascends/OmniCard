using System.Text.Json;
using OmniCard.Models;

namespace OmniCard.Tests.Models;

public class OptcgApiDeserializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public void Deserialize_SetDetail_MapsCardsAndVariants()
    {
        var json = """
        {
          "data": {
            "code": "OP01",
            "name": "Romance Dawn",
            "released_at": "2022-12-02T00:00:00.000Z",
            "card_count": 121,
            "products": [],
            "cards": [
              {
                "card_number": "OP01-001",
                "name": "Roronoa Zoro",
                "language": "en",
                "set": "OP01",
                "set_name": "Romance Dawn",
                "released_at": "2022-12-02T00:00:00.000Z",
                "released": true,
                "card_type": "Leader",
                "rarity": "L",
                "color": ["Red"],
                "cost": null,
                "power": 5000,
                "counter": null,
                "life": 5,
                "attribute": ["Slash"],
                "types": ["Supernovas", "Straw Hat Crew"],
                "effect": "Your turn +1000 power.",
                "trigger": null,
                "block": null,
                "variants": [
                  {
                    "index": 0,
                    "name": null,
                    "label": "Standard",
                    "artist": null,
                    "crop_focus": {"x": null, "y": null},
                    "product": {"id": null, "slug": null, "name": null, "set_code": null, "released_at": null},
                    "images": {
                      "stock": {"full": "https://cdn.poneglyph.one/OP01-001/stock/full.png", "thumb": "https://cdn.poneglyph.one/OP01-001/stock/thumb.webp"},
                      "scan": {"display": null, "full": null, "thumb": null}
                    },
                    "errata": [],
                    "market": {"tcgplayer_url": "https://tcg/x", "market_price": "6.00", "low_price": "1.46", "mid_price": "6.80", "high_price": "34.99"}
                  },
                  {
                    "index": 1,
                    "name": null,
                    "label": "Alternate Art",
                    "artist": "Some Artist",
                    "crop_focus": {"x": 0.5, "y": 0.5},
                    "product": {"id": null, "slug": null, "name": null, "set_code": null, "released_at": null},
                    "images": {
                      "stock": {"full": "https://cdn.poneglyph.one/OP01-001/stock/full-1.png", "thumb": null},
                      "scan": {"display": "https://cdn.poneglyph.one/OP01-001/scan/display-1.png", "full": null, "thumb": null}
                    },
                    "errata": [],
                    "market": {"tcgplayer_url": null, "market_price": "40.00", "low_price": "25.00", "mid_price": "41.00", "high_price": "99.00"}
                  }
                ]
              }
            ]
          }
        }
        """;

        var resp = JsonSerializer.Deserialize<OptcgSetDetailResponse>(json, JsonOptions)!;

        Assert.Equal("OP01", resp.Data.Code);
        Assert.Single(resp.Data.Cards);
        var card = resp.Data.Cards[0];
        Assert.Equal("OP01-001", card.CardNumber);
        Assert.Equal("Roronoa Zoro", card.Name);
        Assert.Equal("Leader", card.CardType);
        Assert.Equal(["Red"], card.Color);
        Assert.Null(card.Cost);
        Assert.Equal(5000, card.Power);
        Assert.Equal(5, card.Life);
        Assert.Equal(["Slash"], card.Attribute);
        Assert.Equal(["Supernovas", "Straw Hat Crew"], card.Types);
        Assert.Equal("Your turn +1000 power.", card.Effect);

        Assert.Equal(2, card.Variants.Count);
        var v0 = card.Variants[0];
        Assert.Equal(0, v0.Index);
        Assert.Equal("Standard", v0.Label);
        Assert.Equal("https://cdn.poneglyph.one/OP01-001/stock/full.png", v0.Images.Stock.Full);
        Assert.Null(v0.Images.Scan.Display);
        Assert.Equal("6.00", v0.Market.MarketPrice);
        Assert.Equal("1.46", v0.Market.LowPrice);

        var v1 = card.Variants[1];
        Assert.Equal(1, v1.Index);
        Assert.Equal("Some Artist", v1.Artist);
        Assert.Equal("https://cdn.poneglyph.one/OP01-001/scan/display-1.png", v1.Images.Scan.Display);
    }

    [Fact]
    public void Deserialize_SetList_MapsSummaries()
    {
        var json = """
        {"data":[{"code":"OP01","name":"Romance Dawn","released_at":"2022-12-02T00:00:00.000Z","card_count":121}]}
        """;
        var resp = JsonSerializer.Deserialize<OptcgSetListResponse>(json, JsonOptions)!;
        Assert.Single(resp.Data);
        Assert.Equal("OP01", resp.Data[0].Code);
        Assert.Equal(121, resp.Data[0].CardCount);
    }
}
