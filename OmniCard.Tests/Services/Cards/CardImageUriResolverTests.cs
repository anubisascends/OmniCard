using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;

namespace OmniCard.Tests.Services.Cards;

public class CardImageUriResolverTests
{
    [Fact]
    public void ScryfallCard_PrefersNormal()
    {
        var card = new Card { ImageUris = new ImageUris { Normal = "n.png", Large = "l.png" } };
        Assert.Equal("n.png", CardImageUriResolver.From(card));
    }

    [Fact]
    public void ScryfallCard_FallsBackToLarge_WhenNormalMissing()
    {
        var card = new Card { ImageUris = new ImageUris { Normal = null, Large = "l.png" } };
        Assert.Equal("l.png", CardImageUriResolver.From(card));
    }

    [Fact]
    public void ScryfallCard_NullImageUris_ReturnsNull()
    {
        var card = new Card { ImageUris = null };
        Assert.Null(CardImageUriResolver.From(card));
    }

    [Fact]
    public void OptcgCard_ReturnsCardImageUri()
    {
        var card = new OptcgCard { CardImageUri = "op.png" };
        Assert.Equal("op.png", CardImageUriResolver.From(card));
    }

    [Fact]
    public void Null_ReturnsNull() => Assert.Null(CardImageUriResolver.From(null));

    [Fact]
    public void UnknownType_ReturnsNull() => Assert.Null(CardImageUriResolver.From("not a card"));
}
