using OmniCard.CardMatching;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Matching;

namespace OmniCard.Tests.Services.Matching;

public class CardAttributeExtractorTests
{
    [Fact]
    public void ExtractColor_Mtg_SingleColor_ReturnsLetter()
    {
        var card = new Card { Colors = ["W"], TypeLine = "Creature" };
        var match = new CardMatch { Source = card };

        var result = CardAttributeExtractor.ExtractColor(match, CardGame.Mtg);

        Assert.Equal("W", result);
    }

    [Fact]
    public void ExtractColor_Mtg_MultiColor_ReturnsJoined()
    {
        var card = new Card { Colors = ["W", "U"], TypeLine = "Creature" };
        var match = new CardMatch { Source = card };

        var result = CardAttributeExtractor.ExtractColor(match, CardGame.Mtg);

        Assert.Equal("WU", result);
    }

    [Fact]
    public void ExtractColor_Mtg_NoColors_Land_ReturnsLand()
    {
        var card = new Card { Colors = [], TypeLine = "Basic Land — Forest" };
        var match = new CardMatch { Source = card };

        var result = CardAttributeExtractor.ExtractColor(match, CardGame.Mtg);

        Assert.Equal("Land", result);
    }

    [Fact]
    public void ExtractColor_Mtg_NoColors_NotLand_ReturnsColorless()
    {
        var card = new Card { Colors = [], TypeLine = "Artifact" };
        var match = new CardMatch { Source = card };

        var result = CardAttributeExtractor.ExtractColor(match, CardGame.Mtg);

        Assert.Equal("Colorless", result);
    }

    [Fact]
    public void ExtractColor_Mtg_NullColors_ReturnsColorless()
    {
        var card = new Card { Colors = null, TypeLine = "Artifact" };
        var match = new CardMatch { Source = card };

        var result = CardAttributeExtractor.ExtractColor(match, CardGame.Mtg);

        Assert.Equal("Colorless", result);
    }

    [Fact]
    public void ExtractColor_OnePiece_ReturnCardColor()
    {
        var card = new OptcgCard { CardColor = "Green", CardType = "Character" };
        var match = new CardMatch { Source = card };

        var result = CardAttributeExtractor.ExtractColor(match, CardGame.OnePiece);

        Assert.Equal("Green", result);
    }

    // MTG CardType stores the FULL type line so subtype/supertype search (t:vampire, t:legendary) works.
    [Fact]
    public void ExtractCardType_Mtg_LegendaryCreature_ReturnsFullTypeLine()
    {
        var card = new Card { TypeLine = "Legendary Creature — Human Wizard", Colors = ["U"] };
        var match = new CardMatch { Source = card };

        var result = CardAttributeExtractor.ExtractCardType(match, CardGame.Mtg);

        Assert.Equal("Legendary Creature — Human Wizard", result);
    }

    [Fact]
    public void ExtractCardType_Mtg_Instant()
    {
        var card = new Card { TypeLine = "Instant", Colors = ["R"] };
        var match = new CardMatch { Source = card };

        var result = CardAttributeExtractor.ExtractCardType(match, CardGame.Mtg);

        Assert.Equal("Instant", result);
    }

    [Fact]
    public void ExtractCardType_Mtg_LegendaryArtifact_KeepsSupertype()
    {
        var card = new Card { TypeLine = "Legendary Artifact", Colors = [] };
        var match = new CardMatch { Source = card };

        var result = CardAttributeExtractor.ExtractCardType(match, CardGame.Mtg);

        Assert.Equal("Legendary Artifact", result);
    }

    [Fact]
    public void ExtractCardType_Mtg_EnchantmentCreature_KeepsSubtype()
    {
        var card = new Card { TypeLine = "Enchantment Creature — God", Colors = ["W"] };
        var match = new CardMatch { Source = card };

        var result = CardAttributeExtractor.ExtractCardType(match, CardGame.Mtg);

        Assert.Equal("Enchantment Creature — God", result);
    }

    [Fact]
    public void ExtractCardType_Mtg_Planeswalker()
    {
        var card = new Card { TypeLine = "Legendary Planeswalker — Jace", Colors = ["U"] };
        var match = new CardMatch { Source = card };

        var result = CardAttributeExtractor.ExtractCardType(match, CardGame.Mtg);

        Assert.Equal("Legendary Planeswalker — Jace", result);
    }

    [Fact]
    public void ExtractCardType_OnePiece_ReturnsCardType()
    {
        var card = new OptcgCard { CardColor = "Red", CardType = "Character" };
        var match = new CardMatch { Source = card };

        var result = CardAttributeExtractor.ExtractCardType(match, CardGame.OnePiece);

        Assert.Equal("Character", result);
    }
}
