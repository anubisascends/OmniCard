using System.Text.Json;
using OmniCard.Models;

namespace OmniCard.Tests.Models;

public class CardDeserializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public void Deserialize_SingleFaceCard_MapsAllFields()
    {
        var json = """
        {
            "id": "0000579f-7b35-4ed3-b44c-db2a538066fe",
            "oracle_id": "44623693-51d6-49ad-8cd7-140505caf02f",
            "multiverse_ids": [457145],
            "name": "Fury Sliver",
            "lang": "en",
            "released_at": "2006-10-06",
            "uri": "https://api.scryfall.com/cards/0000579f",
            "scryfall_uri": "https://scryfall.com/card/tsp/157",
            "layout": "normal",
            "highres_image": true,
            "image_status": "highres_scan",
            "image_uris": {
                "small": "https://cards.scryfall.io/small/fury.jpg",
                "normal": "https://cards.scryfall.io/normal/fury.jpg",
                "large": "https://cards.scryfall.io/large/fury.jpg",
                "png": "https://cards.scryfall.io/png/fury.png",
                "art_crop": "https://cards.scryfall.io/art_crop/fury.jpg",
                "border_crop": "https://cards.scryfall.io/border_crop/fury.jpg"
            },
            "mana_cost": "{5}{R}",
            "cmc": 6.0,
            "type_line": "Creature \u2014 Sliver",
            "oracle_text": "All Sliver creatures have double strike.",
            "power": "3",
            "toughness": "3",
            "colors": ["R"],
            "color_identity": ["R"],
            "keywords": [],
            "legalities": {
                "standard": "not_legal",
                "modern": "legal",
                "commander": "legal"
            },
            "games": ["paper", "mtgo"],
            "reserved": false,
            "game_changer": false,
            "foil": true,
            "nonfoil": true,
            "finishes": ["nonfoil", "foil"],
            "oversized": false,
            "promo": false,
            "reprint": false,
            "variation": false,
            "set_id": "c1d109bc-f5c0-4d3f-9fea-7102bf36afed",
            "set": "tsp",
            "set_name": "Time Spiral",
            "set_type": "expansion",
            "set_uri": "https://api.scryfall.com/sets/c1d109bc",
            "set_search_uri": "https://api.scryfall.com/cards/search?q=set:tsp",
            "scryfall_set_uri": "https://scryfall.com/sets/tsp",
            "rulings_uri": "https://api.scryfall.com/cards/0000579f/rulings",
            "prints_search_uri": "https://api.scryfall.com/cards/search?q=oracleid:44623693",
            "collector_number": "157",
            "digital": false,
            "rarity": "uncommon",
            "flavor_text": "A little fury goes a long way.",
            "artist": "Paolo Parente",
            "artist_ids": ["d48dd097-720e-476a-b7b2-1259861b37f9"],
            "illustration_id": "2fcca987-364c-4738-a75b-099d8a26d614",
            "border_color": "black",
            "frame": "2003",
            "full_art": false,
            "textless": false,
            "booster": true,
            "story_spotlight": false,
            "edhrec_rank": 5765,
            "prices": {
                "usd": "0.35",
                "usd_foil": "1.25",
                "usd_etched": null,
                "eur": "0.10",
                "eur_foil": "0.50",
                "tix": "0.02"
            },
            "related_uris": {
                "gatherer": "https://gatherer.wizards.com/Pages/Card/Details.aspx?multiverseid=457145",
                "edhrec": "https://edhrec.com/route/?cc=Fury+Sliver"
            },
            "purchase_uris": {
                "tcgplayer": "https://tcgplayer.com/fury-sliver",
                "cardmarket": "https://cardmarket.com/fury-sliver"
            }
        }
        """;

        var card = JsonSerializer.Deserialize<Card>(json, JsonOptions)!;

        Assert.Equal(Guid.Parse("0000579f-7b35-4ed3-b44c-db2a538066fe"), card.Id);
        Assert.Equal(Guid.Parse("44623693-51d6-49ad-8cd7-140505caf02f"), card.OracleId);
        Assert.Equal("Fury Sliver", card.Name);
        Assert.Equal("{5}{R}", card.ManaCost);
        Assert.Equal(6.0, card.Cmc);
        Assert.Equal("Creature \u2014 Sliver", card.TypeLine);
        Assert.Equal("3", card.Power);
        Assert.Equal("3", card.Toughness);
        Assert.Equal(["R"], card.Colors);
        Assert.Equal("tsp", card.SetCode);
        Assert.Equal("Time Spiral", card.SetName);
        Assert.Equal("uncommon", card.Rarity);
        Assert.Equal("157", card.CollectorNumber);
        Assert.Equal(5765, card.EdhrecRank);
        Assert.NotNull(card.ImageUris);
        Assert.Equal("https://cards.scryfall.io/normal/fury.jpg", card.ImageUris.Normal);
        Assert.Equal("https://cards.scryfall.io/art_crop/fury.jpg", card.ImageUris.ArtCrop);
        Assert.NotNull(card.Prices);
        Assert.Equal("0.35", card.Prices.Usd);
        Assert.Equal("1.25", card.Prices.UsdFoil);
        Assert.Null(card.Prices.UsdEtched);
        Assert.Equal("legal", card.Legalities["commander"]);
        Assert.Equal("not_legal", card.Legalities["standard"]);
        Assert.Contains(457145, card.MultiverseIds!);
        Assert.Null(card.CardFaces);
        Assert.Null(card.AllParts);
    }

    [Fact]
    public void Deserialize_MultiFaceCard_PopulatesCardFaces()
    {
        var json = """
        {
            "id": "c8b432a7-53da-4f72-a40d-f626689b05e8",
            "oracle_id": "b34bb2dc-c1af-4d77-b0b3-a0fb342a5fc6",
            "name": "Delver of Secrets // Insectile Aberration",
            "lang": "en",
            "released_at": "2011-09-30",
            "uri": "https://api.scryfall.com/cards/c8b432a7",
            "scryfall_uri": "https://scryfall.com/card/isd/51",
            "layout": "transform",
            "highres_image": true,
            "image_status": "highres_scan",
            "cmc": 1.0,
            "type_line": "Creature \u2014 Human Wizard // Creature \u2014 Human Insect",
            "color_identity": ["U"],
            "keywords": ["Transform"],
            "legalities": {"standard": "not_legal", "modern": "legal"},
            "games": ["paper", "mtgo"],
            "reserved": false,
            "game_changer": false,
            "foil": true,
            "nonfoil": true,
            "finishes": ["nonfoil", "foil"],
            "oversized": false,
            "promo": false,
            "reprint": true,
            "variation": false,
            "set_id": "b590ec56-f81e-4b1c-a799-8d498eb6e1f5",
            "set": "isd",
            "set_name": "Innistrad",
            "set_type": "expansion",
            "set_uri": "https://api.scryfall.com/sets/b590ec56",
            "set_search_uri": "https://api.scryfall.com/cards/search?q=set:isd",
            "scryfall_set_uri": "https://scryfall.com/sets/isd",
            "rulings_uri": "https://api.scryfall.com/cards/c8b432a7/rulings",
            "prints_search_uri": "https://api.scryfall.com/cards/search?q=oracleid:b34bb2dc",
            "collector_number": "51",
            "digital": false,
            "rarity": "common",
            "border_color": "black",
            "frame": "2003",
            "full_art": false,
            "textless": false,
            "booster": true,
            "story_spotlight": false,
            "prices": {"usd": "1.00", "usd_foil": null, "usd_etched": null, "eur": null, "eur_foil": null, "tix": null},
            "related_uris": {},
            "purchase_uris": {},
            "card_faces": [
                {
                    "name": "Delver of Secrets",
                    "mana_cost": "{U}",
                    "type_line": "Creature \u2014 Human Wizard",
                    "oracle_text": "At the beginning of your upkeep, look at the top card of your library.",
                    "colors": ["U"],
                    "power": "1",
                    "toughness": "1",
                    "artist": "Matt Stewart",
                    "artist_id": "18015838-148d-4ba7-9ea4-7a1348263e31",
                    "illustration_id": "3b5eb8ad-e4f1-487d-b0f9-9e96d77be2cb",
                    "image_uris": {
                        "small": "https://cards.scryfall.io/small/delver-front.jpg",
                        "normal": "https://cards.scryfall.io/normal/delver-front.jpg",
                        "large": null,
                        "png": null,
                        "art_crop": null,
                        "border_crop": null
                    }
                },
                {
                    "name": "Insectile Aberration",
                    "mana_cost": "",
                    "type_line": "Creature \u2014 Human Insect",
                    "oracle_text": "Flying",
                    "colors": ["U"],
                    "color_indicator": ["U"],
                    "power": "3",
                    "toughness": "2",
                    "artist": "Nils Hamm",
                    "artist_id": "fa380b9c-7c04-42a7-8ba8-20bfb6384948",
                    "illustration_id": "15f39320-5675-497a-ab85-a3857b8ac6dd",
                    "image_uris": {
                        "small": "https://cards.scryfall.io/small/delver-back.jpg",
                        "normal": "https://cards.scryfall.io/normal/delver-back.jpg",
                        "large": null,
                        "png": null,
                        "art_crop": null,
                        "border_crop": null
                    }
                }
            ]
        }
        """;

        var card = JsonSerializer.Deserialize<Card>(json, JsonOptions)!;

        Assert.NotNull(card.CardFaces);
        Assert.Equal(2, card.CardFaces.Count);
        Assert.Equal("Delver of Secrets", card.CardFaces[0].Name);
        Assert.Equal("{U}", card.CardFaces[0].ManaCost);
        Assert.Equal("1", card.CardFaces[0].Power);
        Assert.Equal("1", card.CardFaces[0].Toughness);
        Assert.NotNull(card.CardFaces[0].ImageUris);
        Assert.Equal("https://cards.scryfall.io/normal/delver-front.jpg", card.CardFaces[0].ImageUris!.Normal);
        Assert.Equal(Guid.Parse("18015838-148d-4ba7-9ea4-7a1348263e31"), card.CardFaces[0].ArtistId);
        Assert.Null(card.ManaCost);
        Assert.Null(card.OracleText);
        Assert.Null(card.ImageUris);
        Assert.Null(card.Colors);
    }

    [Fact]
    public void Deserialize_CardWithAllParts_PopulatesAllParts()
    {
        var json = """
        {
            "id": "aaaa579f-7b35-4ed3-b44c-db2a538066fe",
            "oracle_id": "44623693-51d6-49ad-8cd7-140505caf02f",
            "name": "Brood Monitor",
            "lang": "en",
            "released_at": "2015-10-02",
            "uri": "https://api.scryfall.com/cards/aaaa579f",
            "scryfall_uri": "https://scryfall.com/card/bfz/164",
            "layout": "normal",
            "highres_image": true,
            "image_status": "highres_scan",
            "mana_cost": "{4}{G}{G}",
            "cmc": 6.0,
            "type_line": "Creature \u2014 Eldrazi Drone",
            "oracle_text": "When Brood Monitor enters, create three 1/1 Eldrazi Scion tokens.",
            "power": "3",
            "toughness": "3",
            "colors": ["G"],
            "color_identity": ["G"],
            "keywords": [],
            "legalities": {"modern": "legal"},
            "games": ["paper"],
            "reserved": false,
            "game_changer": false,
            "foil": false,
            "nonfoil": true,
            "finishes": ["nonfoil"],
            "oversized": false,
            "promo": false,
            "reprint": false,
            "variation": false,
            "set_id": "91719374-7ac5-4afa-ada8-5fc2a7e0a1b8",
            "set": "bfz",
            "set_name": "Battle for Zendikar",
            "set_type": "expansion",
            "set_uri": "https://api.scryfall.com/sets/91719374",
            "set_search_uri": "https://api.scryfall.com/cards/search?q=set:bfz",
            "scryfall_set_uri": "https://scryfall.com/sets/bfz",
            "rulings_uri": "https://api.scryfall.com/cards/aaaa579f/rulings",
            "prints_search_uri": "https://api.scryfall.com/cards/search?q=oracleid:44623693",
            "collector_number": "164",
            "digital": false,
            "rarity": "uncommon",
            "border_color": "black",
            "frame": "2015",
            "full_art": false,
            "textless": false,
            "booster": true,
            "story_spotlight": false,
            "prices": {"usd": null, "usd_foil": null, "usd_etched": null, "eur": null, "eur_foil": null, "tix": null},
            "related_uris": {},
            "purchase_uris": {},
            "all_parts": [
                {
                    "object": "related_card",
                    "id": "bbbb579f-7b35-4ed3-b44c-db2a538066fe",
                    "component": "combo_piece",
                    "name": "Brood Monitor",
                    "type_line": "Creature \u2014 Eldrazi Drone",
                    "uri": "https://api.scryfall.com/cards/bbbb579f"
                },
                {
                    "object": "related_card",
                    "id": "cccc579f-7b35-4ed3-b44c-db2a538066fe",
                    "component": "token",
                    "name": "Eldrazi Scion",
                    "type_line": "Token Creature \u2014 Eldrazi Scion",
                    "uri": "https://api.scryfall.com/cards/cccc579f"
                }
            ]
        }
        """;

        var card = JsonSerializer.Deserialize<Card>(json, JsonOptions)!;

        Assert.NotNull(card.AllParts);
        Assert.Equal(2, card.AllParts.Count);
        Assert.Equal("token", card.AllParts[1].Component);
        Assert.Equal("Eldrazi Scion", card.AllParts[1].Name);
        Assert.Equal(Guid.Parse("cccc579f-7b35-4ed3-b44c-db2a538066fe"), card.AllParts[1].Id);
    }
}
