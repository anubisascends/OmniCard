using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class CardBackAssetsTests
{
    [Theory]
    [InlineData(CardGame.Mtg, "mtg")]
    [InlineData(CardGame.OnePiece, "optcg")]
    [InlineData(CardGame.Riftbound, "riftbound")]
    [InlineData(CardGame.Pokemon, "pokemon")]
    [InlineData(CardGame.YuGiOh, "yugioh")]
    [InlineData(CardGame.FinalFantasy, "fftcg")]
    public void Slug_CoversEveryGame(CardGame game, string expected)
    {
        Assert.Equal(expected, CardBackAssets.Slug(game));
    }

    [Theory]
    // 3×3 grid: each column mirrors across the row's centre. Middle column is its own mirror.
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    [InlineData(3, 5)]
    [InlineData(4, 4)]
    [InlineData(5, 3)]
    [InlineData(6, 8)]
    [InlineData(7, 7)]
    [InlineData(8, 6)]
    public void MirrorSlot_MirrorsColumnWithinRow(int slot, int expected)
    {
        Assert.Equal(expected, CardBackAssets.MirrorSlot(slot, columns: 3, slotsPerPage: 9));
    }

    [Fact]
    public void MirrorSlot_RaggedLastRow_ReturnsNullWhenMirrorFallsOffPage()
    {
        // 8 pockets, 3 columns → last row holds only slots 6,7. Slot 6 would mirror to index 8,
        // which doesn't exist, so there's no pocket behind it.
        Assert.Null(CardBackAssets.MirrorSlot(6, columns: 3, slotsPerPage: 8));
        Assert.Equal(6, CardBackAssets.MirrorSlot(8, columns: 3, slotsPerPage: 9));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void MirrorSlot_OutOfRangeSlot_ReturnsNull(int slot)
    {
        Assert.Null(CardBackAssets.MirrorSlot(slot, columns: 3, slotsPerPage: 9));
    }

    [Fact]
    public void ReverseCardFor_ReturnsMirroredPocketOccupant()
    {
        // A card in reverse-page slot 2 sits behind front-page slot 0 (mirrored across the row).
        var reverse = new List<CollectionCard>
        {
            new() { Slot = 2, Game = CardGame.YuGiOh, Name = "Behind Left" },
            new() { Slot = 4, Game = CardGame.Mtg, Name = "Behind Middle" },
        };

        var behind0 = CardBackAssets.ReverseCardFor(0, columns: 3, slotsPerPage: 9, reverse);
        Assert.NotNull(behind0);
        Assert.Equal(CardGame.YuGiOh, behind0!.Game);

        // Slot 4 mirrors to itself → the middle card is behind it.
        Assert.Equal("Behind Middle", CardBackAssets.ReverseCardFor(4, 3, 9, reverse)!.Name);

        // Front slot 1 mirrors to reverse slot 1, which is empty → nothing behind it.
        Assert.Null(CardBackAssets.ReverseCardFor(1, 3, 9, reverse));
    }

    [Fact]
    public void ReverseCardFor_EmptyReverse_ReturnsNull()
    {
        Assert.Null(CardBackAssets.ReverseCardFor(0, columns: 3, slotsPerPage: 9, new List<CollectionCard>()));
    }
}
