using System.Text.Json;
using System.Text.Json.Serialization;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class RiftboundApiModelsTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // Verbatim shape from api.riftcodex.com/cards (one base + one alt-art item).
    private const string CardListJson = """
    {"items":[
      {"id":"69bc5bd2d308c64675ca879d","name":"Cull the Weak","riftbound_id":"ogn-209-298",
       "tcgplayer_id":"653002","collector_number":209,
       "attributes":{"energy":2,"might":null,"power":1},
       "classification":{"type":"Spell","supertype":null,"rarity":"Common","domain":["Order"]},
       "text":{"rich":"<p>x</p>","plain":"Each player kills one of their units.","flavour":"Flav."},
       "set":{"set_id":"OGN","label":"Origins"},
       "media":{"image_url":"https://cdn/x.png","artist":"Kudos","accessibility_text":"alt"},
       "tags":[],"orientation":"portrait",
       "metadata":{"clean_name":"Cull the Weak","updated_on":"2026-07-10T22:44:35Z",
                   "alternate_art":false,"overnumbered":false,"signature":false},"new":false},
      {"id":"aaaa1111bbbb2222cccc3333","name":"Vex","riftbound_id":"ogn-310*-298",
       "tcgplayer_id":null,"collector_number":310,
       "attributes":{"energy":4,"might":null,"power":4},
       "classification":{"type":"Legend","supertype":null,"rarity":"Epic","domain":["Body","Order"]},
       "text":{"rich":null,"plain":null,"flavour":null},
       "set":{"set_id":"OGN","label":"Origins"},
       "media":{"image_url":"https://cdn/vex.png","artist":"Splash","accessibility_text":null},
       "tags":[],"orientation":"landscape",
       "metadata":{"clean_name":"Vex","updated_on":"2026-07-10T22:44:35Z",
                   "alternate_art":true,"overnumbered":true,"signature":false},"new":true}
    ],"total":352,"page":1,"size":50,"pages":8}
    """;

    private const string SetListJson = """
    {"items":[
      {"id":"69bc5bf6e195be3e561d1eae","name":"Unleashed","set_id":"UNL","card_count":280,
       "tcgplayer_id":"24560","cardmarket_id":null,"published_on":"2026-05-08T00:00:00"},
      {"id":"69bc5bf6e195be3e561d1eb1","name":"Origins","set_id":"OGN","card_count":352,
       "tcgplayer_id":"24344","cardmarket_id":"6286","published_on":"2025-10-31T00:00:00"},
      {"id":"69bc5bf6e195be3e561d1eb3","name":"OP Promos","set_id":"OPP","card_count":133,
       "tcgplayer_id":"24343","cardmarket_id":["6322","6483"],"published_on":"2025-10-31T00:00:00"}
    ],"total":3,"page":1,"size":100,"pages":1}
    """;

    [Fact]
    public void DeserializesCardList_IncludingNestedAndAltArt()
    {
        var resp = JsonSerializer.Deserialize<RiftboundCardListResponse>(CardListJson, Options)!;
        Assert.Equal(8, resp.Pages);
        Assert.Equal(2, resp.Items.Count);

        var cull = resp.Items[0];
        Assert.Equal("69bc5bd2d308c64675ca879d", cull.Id);
        Assert.Equal("ogn-209-298", cull.RiftboundId);
        Assert.Equal(209, cull.CollectorNumber);
        Assert.Equal(2, cull.Attributes.Energy);
        Assert.Null(cull.Attributes.Might);
        Assert.Equal("Spell", cull.Classification.Type);
        Assert.Equal(["Order"], cull.Classification.Domain);
        Assert.Equal("OGN", cull.Set.SetId);
        Assert.Equal("Origins", cull.Set.Label);
        Assert.Equal("https://cdn/x.png", cull.Media.ImageUrl);
        Assert.Equal("portrait", cull.Orientation);
        Assert.False(cull.Metadata.AlternateArt);

        var vex = resp.Items[1];
        Assert.Equal("ogn-310*-298", vex.RiftboundId);
        Assert.True(vex.Metadata.AlternateArt);
        Assert.True(vex.Metadata.Overnumbered);
        Assert.Equal("landscape", vex.Orientation);
        Assert.Equal(["Body", "Order"], vex.Classification.Domain);
    }

    [Fact]
    public void DeserializesSetList_IgnoringHeterogeneousCardmarketId()
    {
        var resp = JsonSerializer.Deserialize<RiftboundSetListResponse>(SetListJson, Options)!;
        Assert.Equal(3, resp.Items.Count);
        Assert.Equal("UNL", resp.Items[0].SetId);
        Assert.Equal(280, resp.Items[0].CardCount);
        Assert.Equal("Origins", resp.Items[1].Name);
    }
}
